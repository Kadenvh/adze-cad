# Discovery Brief: SOLIDWORKS Write API for Adze

**Date:** 2026-03-15
**Mode:** API research
**Status:** Research complete, pending implementation decisions

---

## Purpose

This document captures the SOLIDWORKS COM API surface relevant to write-capable tools that Adze could implement in Wave 2 (safe writes) and beyond. It is scoped to the interop assemblies already referenced by the project (`SolidWorks.Interop.sldworks`, `SolidWorks.Interop.swconst`, `SolidWorks.Interop.swpublished`) and the in-process COM execution model established by `Adze.Host`.

## Current Read Patterns Established in the Codebase

The existing code in `SessionContextBuilder.cs` demonstrates the COM access patterns that write tools must follow:

- All COM access happens in-process via `ISldWorks` and `ModelDoc2` obtained from the add-in lifecycle
- COM child objects are released via `Marshal.ReleaseComObject()` in `finally` blocks
- Traversal uses `IFirstFeature()` / `IGetNextFeature()` iteration with bounded limits
- Dimension reads use `DisplayDimension` -> `IGetDimension()` -> `GetUserValueIn(model)`
- Custom property reads use `ModelDocExtension.get_CustomPropertyManager(configName)` -> `Get6()`
- Feature suppression state reads use `IsSuppressed2()` with `swThisConfiguration`
- All COM failures are caught and logged without crashing the host

**Referenced interop DLLs (from `Adze.Host.csproj`):**
- `SolidWorks.Interop.sldworks.dll` (primary API surface)
- `SolidWorks.Interop.swconst.dll` (enumerations)
- `SolidWorks.Interop.swpublished.dll` (add-in registration interfaces)

---

## 1. Dimension Modification

### Key COM Interfaces and Methods

**Reading (already implemented):**
```csharp
DisplayDimension displayDim = feature.GetFirstDisplayDimension();
Dimension dim = displayDim.IGetDimension();
double value = dim.GetUserValueIn(model);  // returns in document units
string fullName = dim.FullName;            // e.g. "D1@Boss-Extrude1@Part1.SLDPRT"
```

**Writing:**
```csharp
// Primary method - sets value in system units (meters, radians)
// Returns swSetValueReturnStatus_e
int status = dim.SetSystemValue3(
    double value,                          // new value in SI (meters for length, radians for angle)
    int inConfig,                          // swSetValueInConfiguration_e
    string[] configNames                   // null for current config
);

// Convenience overload for current configuration
int status = dim.SetSystemValue2(
    double value,                          // SI units
    int inConfig                           // swSetValueInConfiguration_e.swSetValue_InThisConfiguration
);

// User-units alternative (matches what GetUserValueIn returns)
int status = dim.SetUserValueIn(
    ModelDoc2 model,
    double value                           // in document display units
);
```

**Return codes (`swSetValueReturnStatus_e`):**
| Value | Name | Meaning |
|-------|------|---------|
| 0 | `swSetValue_Successful` | Value accepted |
| 1 | `swSetValue_InvalidValue` | Value out of range or invalid |
| 2 | `swSetValue_InternalError` | COM-level failure |

**Configuration scope (`swSetValueInConfiguration_e`):**
| Value | Name | Use |
|-------|------|-----|
| 1 | `swSetValue_InThisConfiguration` | Current configuration only |
| 2 | `swSetValue_InAllConfigurations` | All configurations |
| 3 | `swSetValue_InSpecifiedConfigurations` | Named configurations (requires configNames array) |

### Dimension Lookup by Name

The existing read code traverses features to find dimensions. For write operations, a direct lookup is more efficient:

```csharp
// Direct dimension access by full name
// Full name format: "DimName@FeatureName" or "DimName@FeatureName@ConfigName"
Dimension dim = (Dimension)model.Parameter(dimensionFullName);
if (dim != null)
{
    int status = dim.SetSystemValue3(newValue,
        (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,
        null);
}
```

### Unit Conversion Consideration

`SetSystemValue3` expects SI units (meters, radians). `SetUserValueIn` accepts document display units. For Adze, the safest pattern is:

1. Accept user input in document display units (what `get_dimensions` returns via `GetUserValueIn`)
2. Use `SetUserValueIn(model, value)` to avoid manual unit conversion errors
3. Alternatively, read `IModelDoc2.GetUnits()` to convert manually

### Rebuild Requirement

Changing a dimension value does **not** automatically rebuild the model. After setting a value:

```csharp
// Rebuild is required to see geometric effects
bool rebuildResult = model.ForceRebuild3(
    false  // topOnly: false = full rebuild, true = top-level only
);
// Returns true if rebuild succeeded

// Or the lighter-weight variant:
model.EditRebuild3();
```

### Safety Notes

- Setting a dimension to an impossible value (e.g., negative length) returns `swSetValue_InvalidValue` but does not corrupt the document
- Driven dimensions (dimensions controlled by relations or equations) cannot be set directly; `SetSystemValue3` will return an error
- Dimension changes are recorded in the SOLIDWORKS undo stack automatically
- The document becomes dirty (`GetSaveFlag() == true`) after any dimension change

---

## 2. Custom Property Modification

### Key COM Interfaces and Methods

**Reading (already implemented):**
```csharp
ModelDocExtension ext = model.Extension;
CustomPropertyManager manager = ext.get_CustomPropertyManager("");           // document-level
CustomPropertyManager cfgManager = ext.get_CustomPropertyManager("Default"); // config-level
manager.Get6(propertyName, true, out string rawVal, out string resolvedVal,
             out bool wasResolved, out bool linkToProperty);
```

**Writing - Add:**
```csharp
// Add3 returns swCustomInfoAddResult_e
int addResult = manager.Add3(
    string fieldName,       // property name
    int fieldType,          // swCustomInfoType_e (e.g., swCustomInfoText = 30)
    string fieldValue,      // the value string
    int overwriteExisting   // swCustomPropertyAddOption_e
);
```

**`swCustomInfoType_e` values:**
| Value | Name | Use |
|-------|------|-----|
| 30 | `swCustomInfoText` | Free-form text |
| 31 | `swCustomInfoDate` | Date value |
| 32 | `swCustomInfoNumber` | Numeric value |
| 33 | `swCustomInfoYesOrNo` | Boolean yes/no |

**`swCustomPropertyAddOption_e` values:**
| Value | Name | Behavior |
|-------|------|----------|
| 0 | `swCustomPropertyOnlyIfNew` | Fails if property already exists |
| 1 | `swCustomPropertyReplaceValue` | Overwrites existing value |

**`swCustomInfoAddResult_e` return values:**
| Value | Name | Meaning |
|-------|------|---------|
| 0 | `swCustomInfoAddResult_AddedOrChanged` | Success |
| 1 | `swCustomInfoAddResult_AlreadyExists` | Property exists and overwrite was not requested |
| 2 | `swCustomInfoAddResult_GenericFail` | Internal failure |
| 3 | `swCustomInfoAddResult_InvalidFieldType` | Bad type enum |

**Writing - Modify existing:**
```csharp
// Set2 modifies an existing property's value
// Returns swCustomInfoSetResult_e
int setResult = manager.Set2(
    string fieldName,
    string fieldValue
);
```

**Writing - Delete:**
```csharp
// Delete2 removes a property by name
// Returns swCustomInfoDeleteResult_e
int deleteResult = manager.Delete2(
    string fieldName
);
```

### Scope Pattern

Custom properties exist at two levels. The manager is obtained per-scope:

```csharp
// Document-level properties (shared across all configurations)
CustomPropertyManager docManager = ext.get_CustomPropertyManager("");

// Configuration-specific properties
CustomPropertyManager cfgManager = ext.get_CustomPropertyManager("ConfigName");
```

### Safety Notes

- Custom property operations are recorded in the undo stack
- Adding a property that already exists with `swCustomPropertyOnlyIfNew` fails silently (returns `AlreadyExists`) rather than throwing
- Deleting a non-existent property returns a failure code but does not throw
- Properties linked to other fields (e.g., `"$PRP:SW-File Name"`) can have their value overwritten, breaking the link
- No rebuild is needed after custom property changes
- The document becomes dirty after any property change

---

## 3. Feature Suppression / Unsuppression

### Key COM Interfaces and Methods

**Reading (already implemented):**
```csharp
bool isSuppressed = feature.IsSuppressed();
// Or the configuration-aware variant:
object state = feature.IsSuppressed2(
    (int)swInConfigurationOpts_e.swThisConfiguration, null);
```

**Writing:**
```csharp
// SetSuppression2 changes suppression state
// Returns bool indicating success
bool result = feature.SetSuppression2(
    int suppressionState,    // swFeatureSuppressionAction_e
    int inConfig,            // swInConfigurationOpts_e
    string[] configNames     // null for current config
);
```

**`swFeatureSuppressionAction_e` values:**
| Value | Name | Effect |
|-------|------|--------|
| 0 | `swSuppressFeature` | Suppress the feature |
| 1 | `swUnSuppressFeature` | Unsuppress (resolve) the feature |
| 2 | `swUnSuppressDependent` | Unsuppress the feature and all its dependents |

**`swInConfigurationOpts_e` values (shared with dimension scope):**
| Value | Name | Use |
|-------|------|-----|
| 1 | `swThisConfiguration` | Current configuration only |
| 2 | `swAllConfiguration` | All configurations |
| 3 | `swSpecifyConfiguration` | Named configurations |

### Rebuild Requirement

Suppression state changes **require a rebuild** for the model to update:

```csharp
feature.SetSuppression2(
    (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
    (int)swInConfigurationOpts_e.swThisConfiguration,
    null);

model.EditRebuild3();  // required to see geometric effect
```

### Safety Notes

- Suppressing a parent feature automatically suppresses its children; unsuppressing a parent does not automatically unsuppress children unless `swUnSuppressDependent` is used
- Suppressing a feature that other features depend on (e.g., a sketch used by an extrude) suppresses the dependents too
- Suppression changes are recorded in the undo stack
- Suppressing/unsuppressing in an assembly can affect component visibility and mate resolution
- The operation can fail silently if the feature is in an edit state or if the configuration is read-only
- Feature lookup by name: `model.FeatureByName(featureName)` returns `Feature` or `null`

---

## 4. Undo / Redo

### Key COM Interfaces and Methods

**Single-step undo:**
```csharp
// EditUndo2 undoes the last operation
// count parameter: number of steps to undo
bool result = model.EditUndo2(int count);
// Returns true if successful
```

**Single-step redo:**
```csharp
bool result = model.EditRedo2(int count);
```

**Undo stack inspection (limited):**
```csharp
// ClearUndoList clears the entire undo history
// WARNING: This is destructive and irreversible
model.ClearUndoList();
```

### Transaction / Bookmark Mechanism

SOLIDWORKS does **not** expose a formal transaction or savepoint API through COM. There is no `BeginTransaction()` / `CommitTransaction()` / `RollbackTransaction()` pattern. However, there are practical alternatives:

**User Control grouping (batch undo):**
```csharp
// IModelDocExtension.SetUserPreferenceToggle or the model extension methods
// can group operations, but the primary mechanism is:

// Start a macro feature or use the model's built-in undo grouping:
model.Extension.StartRecordingUndoObject("Adze: set dimension D1");
// ... perform one or more write operations ...
model.Extension.FinishRecordingUndoObject();
```

When `StartRecordingUndoObject` / `FinishRecordingUndoObject` are used, all operations between them appear as a single undo step with the given label. This is the closest equivalent to a transaction bookmark.

**Practical undo pattern for Adze:**
```csharp
ModelDocExtension ext = model.Extension;
ext.StartRecordingUndoObject("Adze: " + operationDescription);
try
{
    // Perform write operation(s)
    int status = dim.SetSystemValue3(newValue,
        (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);

    if (status != (int)swSetValueReturnStatus_e.swSetValue_Successful)
    {
        // Operation failed; undo recording captures nothing meaningful
        return WriteResult.Failed(status);
    }

    model.EditRebuild3();
}
finally
{
    ext.FinishRecordingUndoObject();
}
```

### Safety Notes

- `EditUndo2(1)` is the simplest rollback mechanism after a single write operation
- Undo grouping via `StartRecordingUndoObject` / `FinishRecordingUndoObject` is the recommended pattern for multi-step operations that should roll back atomically
- The undo label string is visible to the user in the Edit menu (e.g., "Undo Adze: set dimension D1")
- There is no way to undo only a specific operation from the middle of the stack
- `ClearUndoList()` is destructive and should never be called by Adze
- Undo is not available after `model.Save()` -- save commits the undo stack
- Some operations (like saving) are not undoable and break the undo chain
- The undo stack has a finite depth controlled by SOLIDWORKS system options

---

## 5. Sketch Creation

### Key COM Interfaces and Methods

**Sketch manager access:**
```csharp
SketchManager sketchMgr = model.SketchManager;
```

**Creating a new sketch:**
```csharp
// Step 1: Select a plane or face to sketch on
// This requires pre-selecting a reference plane or face
bool selected = model.Extension.SelectByID2(
    "Front Plane",                          // entity name
    "PLANE",                                // selection type
    0, 0, 0,                                // pick point (x, y, z)
    false,                                  // append to selection
    0,                                      // mark
    null,                                   // callout
    (int)swSelectOption_e.swSelectOptionDefault
);

// Step 2: Insert a new sketch on the selected plane
sketchMgr.InsertSketch(true);  // true = create new sketch; false = exit sketch edit

// Step 3: Now in sketch edit mode - add geometry
// Lines
SketchSegment line = sketchMgr.CreateLine(
    double x1, double y1, double z1,       // start point (meters)
    double x2, double y2, double z2        // end point (meters)
);

// Circles
SketchSegment circle = sketchMgr.CreateCircle(
    double cx, double cy, double cz,       // center point
    double edgeX, double edgeY, double edgeZ // point on circumference
);

// Arcs
SketchSegment arc = sketchMgr.CreateArc(
    double cx, double cy, double cz,       // center
    double startX, double startY, double startZ,
    double endX, double endY, double endZ,
    short direction                         // 1 = CW, -1 = CCW
);

// Rectangles (corner-corner)
// Returns array of SketchSegment
object[] segments = (object[])sketchMgr.CreateCornerRectangle(
    double x1, double y1, double z1,       // first corner
    double x2, double y2, double z2        // opposite corner
);

// Step 4: Exit sketch edit mode
sketchMgr.InsertSketch(true);  // calling again exits sketch mode
// Or:
model.InsertSketch2(true);     // alternative exit
```

**Alternative sketch creation (explicit):**
```csharp
// Insert3DSketch creates a 3D sketch (no plane selection needed)
sketchMgr.Insert3DSketch(true);
// ... add 3D geometry ...
sketchMgr.Insert3DSketch(true);  // exit
```

### Sketch Constraints / Relations

```csharp
SketchManager sketchMgr = model.SketchManager;

// Add a relation to selected sketch entities
// Requires pre-selecting the entities
model.SketchAddConstraints(
    string relationType  // "sgHORIZONTAL", "sgVERTICAL", "sgCOINCIDENT",
                         // "sgCONCENTRIC", "sgTANGENT", "sgEQUAL", etc.
);
```

### Safety Notes

- Sketch creation is a multi-step process with modal state: the model enters "sketch edit mode"
- If the host crashes or loses context while in sketch edit mode, the sketch remains open and the model is in an inconsistent state
- All sketch geometry coordinates are in **meters** (SI) regardless of document units
- Sketch creation appears as a single undo step only if the sketch open/close is managed properly
- Creating a sketch on an invalid reference (e.g., a suppressed plane) fails silently
- The plane/face **must** be pre-selected before `InsertSketch(true)` is called
- Sketch operations are complex enough that they should be a later-wave tool, not a Wave 2 candidate

---

## 6. Feature Creation (Extrusion)

### Key COM Interfaces and Methods

**Feature manager access:**
```csharp
FeatureManager featMgr = model.FeatureManager;
```

**Boss Extrude (add material):**
```csharp
// Requires an active sketch profile (sketch must be closed/exited first,
// or the sketch must be selected)

// FeatureExtrusion3 is the current recommended method
Feature extrudeFeature = featMgr.FeatureExtrusion3(
    bool sd,                    // true = single direction, false = both
    bool flip,                  // flip direction
    bool dir,                   // direction: true = normal, false = reversed
    int t1,                     // end condition type 1: swEndConditions_e
    int t2,                     // end condition type 2 (for bidirectional)
    double d1,                  // depth 1 (meters)
    double d2,                  // depth 2
    bool dchk1,                // draft on/off direction 1
    bool dchk2,                // draft on/off direction 2
    bool ddir1,                // draft outward direction 1
    bool ddir2,                // draft outward direction 2
    double dang1,              // draft angle 1 (radians)
    double dang2,              // draft angle 2 (radians)
    bool offsetReverse1,       // offset reverse direction 1
    bool offsetReverse2,       // offset reverse direction 2
    bool translateSurface1,    // translate surface direction 1
    bool translateSurface2,    // translate surface direction 2
    bool merge,                // merge result
    bool useFeatScope,         // use feature scope
    bool useAutoSelect,        // auto-select bodies
    int t0,                    // thin feature type: swThinWallType_e (0 = none)
    double thin1,              // thin wall thickness 1
    double thin2,              // thin wall thickness 2
    bool endCapOn,             // end cap for thin feature
    bool flipEndCap,           // flip end cap
    bool optimizeGeometry      // optimize geometry
);
```

**End condition types (`swEndConditions_e`):**
| Value | Name | Meaning |
|-------|------|---------|
| 0 | `swEndCondBlind` | Fixed depth |
| 1 | `swEndCondThroughAll` | Through entire model |
| 2 | `swEndCondThroughAllBoth` | Through all in both directions |
| 5 | `swEndCondUpToSurface` | Up to a selected surface |
| 7 | `swEndCondMidPlane` | Symmetric about sketch plane |

**Simpler alternative for basic extrusions:**
```csharp
// FeatureBossExtrude creates a simpler boss extrude
// But FeatureExtrusion3 is the general-purpose method
```

**Cut Extrude (remove material):**
```csharp
Feature cutFeature = featMgr.FeatureCut4(
    bool sd,                   // single direction
    bool flip,                 // flip
    bool dir,                  // direction
    int type1,                 // end condition 1
    int type2,                 // end condition 2
    double depth1,             // depth 1 (meters)
    double depth2,             // depth 2
    bool draftChk1,            // draft on direction 1
    bool draftChk2,            // draft on direction 2
    bool draftDir1,            // draft outward direction 1
    bool draftDir2,            // draft outward direction 2
    double draftAngle1,        // draft angle 1 (radians)
    double draftAngle2,        // draft angle 2 (radians)
    bool offsetReverse1,
    bool offsetReverse2,
    bool translateSurface1,
    bool translateSurface2,
    bool normalCut,            // normal cut
    bool useFeatScope,
    bool useAutoSelect,
    bool assemblyFeatureScope,
    bool autoSelectComponents,
    bool propagateFeature,
    int t0,                    // thin wall type
    double thin1,
    double thin2,
    bool endCapOn,
    bool optimizeGeometry,
    int flipDir,               // flip cut direction
    bool reverseDir            // reverse cut direction
);
```

### Safety Notes

- Feature creation methods have extremely long parameter lists; incorrect parameter combinations produce unpredictable geometry
- A sketch **must** exist and contain a valid closed profile before an extrusion can be created
- Feature creation requires and triggers a rebuild
- The feature is added to the undo stack automatically
- If the sketch profile is open (not closed), boss extrude fails; cut extrude may succeed with open contours depending on context
- Feature creation is complex enough to be a Wave 3+ tool; it involves sketch creation as a prerequisite
- `FeatureExtrusion3` returns `null` if the operation fails (no exception thrown)

---

## 7. Assembly Operations

### Adding Mates

**Mate creation (`IAssemblyDoc`):**
```csharp
AssemblyDoc assembly = (AssemblyDoc)model;

// Requires pre-selecting two entities (faces, edges, planes, etc.)
// The selection mark identifies which entity is which

// AddMate5 is the current recommended method
// Earlier versions: AddMate3, AddMate4
int mateError = 0;
Mate2 mate = assembly.AddMate5(
    int mateTypeFromEnum,       // swMateType_e
    int mateAlign,              // swMateAlign_e
    bool flip,                  // flip mate direction
    double distance,            // distance value (for distance mates)
    double distAbsUpperLimit,   // upper limit
    double distAbsLowerLimit,   // lower limit
    double gearRatio1,          // gear ratio numerator
    double gearRatio2,          // gear ratio denominator
    double angle,               // angle value (radians, for angle mates)
    double angleAbsUpperLimit,
    double angleAbsLowerLimit,
    bool forPositioningOnly,    // true = don't solve, just position
    bool lockRotation,          // lock rotation
    int widthMateOption,        // swMateWidthOptions_e
    out int errorStatus         // swAddMateError_e
);
```

**Common mate types (`swMateType_e`):**
| Value | Name | Use |
|-------|------|-----|
| 0 | `swMateCOINCIDENT` | Faces/planes flush |
| 1 | `swMateCONCENTRIC` | Cylindrical alignment |
| 2 | `swMatePERPENDICULAR` | 90-degree orientation |
| 3 | `swMatePARALLEL` | Parallel orientation |
| 4 | `swMateTANGENT` | Surface tangency |
| 5 | `swMateDISTANCE` | Fixed distance between entities |
| 6 | `swMateANGLE` | Fixed angle between entities |

**Mate alignment (`swMateAlign_e`):**
| Value | Name |
|-------|------|
| 0 | `swMateAlignALIGNED` |
| 1 | `swMateAlignANTI_ALIGNED` |
| 2 | `swMateAlignCLOSEST` |

### Inserting Components

```csharp
AssemblyDoc assembly = (AssemblyDoc)model;

// AddComponent5 inserts a component into the assembly
Component2 newComponent = assembly.AddComponent5(
    string compPath,            // full file path to part or sub-assembly
    int resolveState,           // swAddComponentConfigOptions_e
    string configName,          // configuration name (empty for default)
    bool newInstance,           // true = always new instance
    string configOption,        // configuration option string
    double x,                   // insertion point X (meters)
    double y,                   // insertion point Y
    double z                    // insertion point Z
);
```

### Safety Notes

- Mate creation requires **exactly two entities pre-selected** with correct selection marks
- Invalid mate combinations (e.g., coincident between two parallel planes that don't intersect) fail and return error codes in `errorStatus` but can leave the assembly in an inconsistent selection state
- Over-constrained assemblies will report errors but may not immediately fail
- Mate operations modify the undo stack
- Component insertion is relatively safe (just places a reference) but the component path must be valid
- Mate operations require a rebuild to see positional effects: `assembly.EditRebuild3()`
- Assembly operations are significantly more complex than part operations and should be later-wave tools

---

## 8. Safety Considerations for Write Operations

### Operations That Can Fail Silently

| Operation | Silent Failure Mode |
|-----------|-------------------|
| `SetSuppression2` | Returns `false` but no exception if feature is in edit state |
| `SetSystemValue3` on driven dimension | Returns error code but no exception |
| `Add3` with `swCustomPropertyOnlyIfNew` | Returns `AlreadyExists` code, not an exception |
| `FeatureExtrusion3` with invalid sketch | Returns `null`, no exception |
| `AddMate5` with wrong selections | Sets `errorStatus` but may not throw |
| `SelectByID2` for nonexistent entity | Returns `false`, no exception |

### Operations That Require Rebuild After Modification

| Operation | Rebuild Needed? | Notes |
|-----------|----------------|-------|
| Dimension change | **Yes** | Geometry does not update until rebuild |
| Custom property change | No | Properties are metadata, no geometry impact |
| Feature suppress/unsuppress | **Yes** | Feature tree updates but geometry needs rebuild |
| Sketch geometry addition | **Yes** (implicit) | Exiting sketch triggers partial rebuild |
| Feature creation | **Yes** (implicit) | Feature creation methods trigger rebuild |
| Mate creation | **Yes** | Assembly positions don't update until rebuild |
| Component insertion | **Yes** | Typically triggers automatic lightweight rebuild |

### Operations That Can Corrupt the Document

| Risk | Scenario | Mitigation |
|------|----------|------------|
| **Open sketch orphan** | Crash during sketch edit mode leaves sketch open | Always wrap sketch open/close in try/finally; verify sketch edit state before and after |
| **Broken mate set** | Adding a mate that over-constrains and then failing to undo | Check mate error return; undo on failure |
| **Invalid rebuild state** | Multiple dimension changes without intermediate rebuilds causing cascading geometry failures | Rebuild after each logical change group |
| **Reference corruption** | Deleting or suppressing features that external references depend on | Check reference graph before suppress/delete operations |
| **Configuration corruption** | Writing dimension values to wrong configurations | Always verify active configuration before write |
| **Equation conflict** | Setting a dimension that is equation-driven | Check `Dimension.DrivenState` before attempting set |

### Recommended Write Tool Safety Pattern

Every write tool in Adze should follow this sequence:

```
1. PREVIEW
   - Read current state (what exists now)
   - Compute proposed change (what will change)
   - Return preview to user without modifying anything
   - Populate ApprovalState = PreviewReady

2. CONFIRM
   - User explicitly approves the previewed change
   - ApprovalState transitions to Approved

3. APPLY
   - Start undo group: ext.StartRecordingUndoObject("Adze: <description>")
   - Execute the COM write operation
   - Check return code for success
   - If failed: finish undo recording, return failure
   - If succeeded: rebuild if needed
   - Finish undo group: ext.FinishRecordingUndoObject()
   - ApprovalState = Executing -> Completed

4. VERIFY
   - Re-read the modified state using existing read tools
   - Compare actual result to expected result
   - Log discrepancies
   - ApprovalState = Verifying -> Completed

5. LOG
   - Record trace event with before/after state
   - Include undo group label for rollback reference
   - Include rebuild state after change

6. ROLLBACK GUIDANCE
   - If verification fails, provide: "Run Edit > Undo or press Ctrl+Z to revert"
   - The undo group label "Adze: <description>" will appear in the Edit menu
   - For multi-step operations, specify the undo count needed
```

---

## 9. Wave 2 Tool Candidates (Recommended First Write Tools)

Based on the analysis above, ordered by implementation safety and complexity:

### Tier 1 - Safest First Implementations

**`set_custom_property`** (ActionClass: Yellow)
- Lowest risk: no rebuild needed, no geometry impact
- Clean COM API: `Add3` / `Set2` / `Delete2` with explicit return codes
- Already read-implemented: `get_custom_properties` uses the same `CustomPropertyManager`
- Fully undoable via SOLIDWORKS undo stack
- Preview is trivial: show current value vs. proposed value

**`set_dimension_value`** (ActionClass: Yellow)
- Moderate risk: requires rebuild, value validation needed
- Clean COM API: `SetUserValueIn` or `SetSystemValue3` with return codes
- Already read-implemented: `get_dimensions` traverses dimensions and reads values
- Must check `DrivenState` before attempting set (driven dimensions cannot be edited)
- Fully undoable via SOLIDWORKS undo stack
- Preview: show current value, proposed value, and unit context

### Tier 2 - Safe With Dependency Awareness

**`suppress_feature`** and **`unsuppress_feature`** (ActionClass: Yellow)
- Moderate risk: suppression cascades to dependent features
- Clean COM API: `SetSuppression2` with clear enum parameters
- Already read-implemented: `get_feature_tree_slice` reads suppression state
- Must warn about dependent features before suppression
- Fully undoable
- Preview: show feature name, current state, and list of dependent features that will also be affected

### Tier 3 - Complex / Later Wave

**Sketch creation, feature creation, mate creation** (ActionClass: Red)
- High complexity: multi-step modal operations, long parameter lists
- Higher corruption risk: sketch edit mode can orphan, mates can over-constrain
- Should wait until the Tier 1/2 tools are proven, traced, and evaluated

---

## 10. Implementation Patterns for Adze

### Proposed Write Tool Interface

```csharp
public interface IWriteTool<in TParameters, TPreview>
{
    string ToolName { get; }
    ActionClass ActionClass { get; }

    // Phase 1: Generate preview without modifying anything
    WritePreview<TPreview> Preview(SessionContext context, TParameters parameters);

    // Phase 2: Execute the write operation (only after approval)
    WriteResult Apply(ISldWorks application, TParameters parameters, string undoLabel);

    // Phase 3: Verify the result matches expectations
    WriteVerification Verify(SessionContext refreshedContext, TParameters parameters);
}
```

### Proposed Undo Integration

```csharp
// Wrapper for consistent undo grouping across all write tools
internal static class UndoScope
{
    public static WriteResult Execute(
        ModelDoc2 model,
        string operationLabel,
        Func<WriteResult> action)
    {
        ModelDocExtension ext = model.Extension;
        ext.StartRecordingUndoObject("Adze: " + operationLabel);
        try
        {
            WriteResult result = action();
            if (result.Success && result.RequiresRebuild)
            {
                model.EditRebuild3();
            }
            return result;
        }
        catch (Exception ex)
        {
            return WriteResult.Failed("COM exception: " + ex.Message);
        }
        finally
        {
            ext.FinishRecordingUndoObject();
        }
    }
}
```

### COM Object Access for Write Tools

Write tools need direct COM access (unlike read tools which operate on serialized `SessionContext`). This means:

- Write tool execution must happen on the host/UI thread (same as `SessionContextBuilder`)
- Write tools receive `ISldWorks` / `ModelDoc2` references, not just `SessionContext`
- The existing `IReadOnlyTool<T>` interface is intentionally inadequate for writes; a new `IWriteTool<T>` boundary is needed
- After write + rebuild, `SessionContext` should be re-captured to reflect the new state

---

## 11. Gaps and Open Questions

1. **`IModelDocExtension.StartRecordingUndoObject` availability**: Confirm this method exists in the R2026x interop. Earlier SOLIDWORKS versions had this; it should be present but needs a build-time check.

2. **Driven dimension detection**: The existing dimension read code does not capture `DrivenState`. The write tool needs `Dimension.DrivenState` (returns `swDimensionDrivenState_e`) to avoid attempting to set driven dimensions.

3. **Feature dependency graph for suppression**: The existing `get_feature_tree_slice` does not capture parent/child relationships. Suppression preview needs to know which features will cascade. `Feature.GetParents()` and `Feature.GetChildren()` provide this.

4. **Equation-driven dimensions**: Dimensions controlled by equations should be flagged during preview. `Dimension.IsDesignTableDimension` and equation manager inspection may be needed.

5. **Assembly-level dimension scope**: In assemblies, dimensions can belong to components. The write tool needs to handle component-level dimension access differently from assembly-level dimensions.

6. **Thread safety**: Write operations must stay on the SOLIDWORKS UI/host thread. The current background execution pattern for model calls must not be extended to write operations.

7. **Read-only document guard**: The existing `DocumentInfo.IsReadOnly` flag should be checked before any write attempt. The COM API may also return errors for read-only documents, but a pre-check is cleaner.

---

## References

- `SolidWorks.Interop.sldworks.dll` (local: `C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\api\redist\`)
- `SolidWorks.Interop.swconst.dll` (enumerations referenced throughout)
- Existing read implementations: `src/Adze.Host/Services/SessionContextBuilder.cs`
- Existing tool contracts: `src/Adze.Contracts/Models/ToolContracts.cs`
- Existing safety model: `src/Adze.Contracts/Enums/ApprovalState.cs`, `src/Adze.Contracts/Enums/ActionClass.cs`
- Wave 2 tool list: `documentation/BUILD_SPEC.md` (Planned Wave 2 Safe Write Tools)
