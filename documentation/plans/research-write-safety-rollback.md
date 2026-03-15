# Research Brief: Write Safety and Rollback Guarantees in the SOLIDWORKS COM API

**Date:** 2026-03-15
**Mode:** De-risking research
**Status:** Complete
**Companion document:** `discovery-solidworks-write-api.md` (method signatures and parameter enums)
**Grounding sources:** Existing read tool implementations in `src/Adze.Tools/Grounding/`, COM access patterns in `src/Adze.Host/Services/SessionContextBuilder.cs`, interop assemblies from `SOLIDWORKS 3DEXPERIENCE R2026x`

---

## Purpose

This document answers a single question: **for each write operation Adze might perform, what are the exact rollback and undo guarantees available through the SOLIDWORKS COM API, and what are the failure modes that could leave a document in a state the user did not request?**

This is not a design proposal. It is a risk inventory. The recommendations at the end are risk classifications, not implementation plans.

---

## 1. The Undo Infrastructure Available to Add-ins

SOLIDWORKS exposes three undo-related mechanisms through the COM API. Understanding their exact guarantees and limitations is the foundation for everything that follows.

### 1.1 Single-Step Undo: `IModelDoc2.EditUndo2(int count)`

| Property | Detail |
|----------|--------|
| **What it does** | Undoes the most recent `count` operations from the undo stack |
| **Return value** | `bool` -- `true` if at least one undo step succeeded |
| **Atomicity** | Each call is atomic; partial undo within a grouped operation is not possible |
| **Availability** | Available on all document types (part, assembly, drawing) |
| **Limitations** | Cannot target a specific operation in the middle of the stack. Cannot undo past a save boundary. The undo stack has a finite depth controlled by `Tools > Options > System Options > General > Undo` (default varies by version, typically 20-50 steps). Once the stack is full, the oldest entries are silently dropped. |
| **Thread safety** | Must be called on the SOLIDWORKS UI/host thread |

### 1.2 Grouped Undo: `IModelDocExtension.StartRecordingUndoObject` / `FinishRecordingUndoObject`

| Property | Detail |
|----------|--------|
| **What it does** | Groups all operations between `Start` and `Finish` into a single undo step with a user-visible label |
| **API** | `void StartRecordingUndoObject(string label)` / `void FinishRecordingUndoObject()` |
| **Label visibility** | The label appears in the Edit menu as "Undo [label]" and in the undo dropdown |
| **Nesting** | **Not supported.** Calling `StartRecordingUndoObject` while a recording is already active produces undefined behavior. The inner group may silently merge into the outer group, or both may be lost. Adze must guarantee single-level grouping only. |
| **Exception during recording** | If an exception occurs between `Start` and `Finish`, calling `FinishRecordingUndoObject` in a `finally` block closes the group. Any operations that executed before the exception are included in the group and are undoable as one step. Operations that did not execute are simply absent from the group. |
| **Empty group** | If no operations execute between `Start` and `Finish`, an empty undo step may appear in the stack. This is harmless but unclean. The implementation should detect this case and avoid emitting a rollback reference for it. |
| **Availability in R2026x** | This method exists on `IModelDocExtension` in the R2026x interop. It has been present since SOLIDWORKS 2010 (API version 18). |
| **Rebuild within a group** | Calling `EditRebuild3()` inside a group is valid. The rebuild itself does not create a separate undo step -- it is absorbed into the group. |

### 1.3 No Transactions, No Savepoints

SOLIDWORKS has **no** equivalent to a database transaction, savepoint, or rollback-to-mark mechanism. Specifically:

- There is no `BeginTransaction()` / `CommitTransaction()` / `RollbackTransaction()` API.
- There is no way to create a named savepoint and later restore to it.
- There is no way to atomically test-and-commit: an operation either succeeds and is recorded, or fails and is not recorded, but there is no way to execute speculatively and then decide whether to keep the result.
- `IModelDoc2.ClearUndoList()` destroys all undo history and must never be called by Adze.

### 1.4 Undo Stack Boundary Conditions

| Condition | Effect on undo |
|-----------|---------------|
| **Document save** (`IModelDoc2.Save3`)| Commits all pending state. Undo stack is preserved across save in SOLIDWORKS 2016+, but saving is irreversible itself -- you cannot undo a save. |
| **Document close/reopen** | Undo stack is destroyed on close. |
| **Configuration switch** | Undo stack is preserved, but undoing a pre-switch operation after switching configurations can produce unexpected geometry if the operation was configuration-scoped. |
| **External file modification** | If a referenced part file is modified externally while the assembly is open, undo behavior for operations that depend on that reference becomes unreliable. |
| **Add-in crash** | Undo stack survives add-in unload/crash because SOLIDWORKS manages it. The user can still undo from the Edit menu after an add-in failure. |
| **SOLIDWORKS crash** | Undo stack is lost. Recovery file may exist. |

---

## 2. Operation-by-Operation Rollback Matrix

### Legend

| Column | Meaning |
|--------|---------|
| **Operation** | The write action under evaluation |
| **API entry points** | Primary COM methods used |
| **Grouped undo supported** | Whether `StartRecordingUndoObject` / `FinishRecordingUndoObject` wrapping works correctly |
| **Reversal deterministic** | Whether `EditUndo2(1)` after a grouped operation restores the exact prior state with no side effects |
| **Before/after snapshot needed** | Whether Adze must capture state before applying the change, beyond what the undo stack provides |
| **Rebuild required** | Whether `EditRebuild3()` must be called for the change to take geometric effect |
| **Failure modes** | Ways the operation can fail or leave the document in an unexpected state |
| **Verification method** | How to confirm the write took effect as intended |
| **Recommendation** | Wave assignment and risk classification |

---

### 2.1 Dimension Value Change

| Attribute | Detail |
|-----------|--------|
| **Operation** | Set a dimension to a new numeric value |
| **API entry points** | `Dimension.SetUserValueIn(ModelDoc2, double)` (document units) or `Dimension.SetSystemValue3(double, int, string[])` (SI units). Dimension lookup via `ModelDoc2.Parameter(fullName)` or feature traversal. |
| **Grouped undo supported** | **Yes.** Dimension value changes are recorded in the SOLIDWORKS undo stack. Wrapping in `StartRecordingUndoObject` / `FinishRecordingUndoObject` works correctly and absorbs the subsequent rebuild into the same group. |
| **Reversal deterministic** | **Yes, with caveats.** `EditUndo2(1)` restores the prior dimension value and triggers an implicit rebuild. The geometry returns to its prior state. **Caveat:** if the dimension change caused a downstream rebuild error (e.g., a fillet that can no longer resolve), undoing the dimension change also undoes the error, restoring the prior clean state. This is correct behavior. **Caveat:** if equations reference this dimension, the equation values update on undo, restoring the prior equation state. This is also correct. |
| **Before/after snapshot needed** | **Recommended but not strictly required for rollback.** The undo stack handles restoration. However, a before-snapshot enables Adze to report what changed in the trace log and to detect verification mismatches. Read the dimension value via `Dimension.GetUserValueIn(model)` before and after. |
| **Rebuild required** | **Yes.** The model does not update geometrically until `EditRebuild3()` is called. |
| **Failure modes** | 1. `swSetValue_InvalidValue` -- value out of valid range (e.g., negative length for a boss extrude depth). No state change occurs. 2. `swSetValue_InternalError` -- COM-level failure. No state change occurs. 3. Attempting to set a **driven dimension** (controlled by a relation, equation, or constraint). `SetSystemValue3` returns an error code. Check `Dimension.DrivenState` beforehand (`swDimensionDrivenState_e`). 4. Attempting to set a dimension on a **suppressed feature**. The dimension object may be obtainable but the value change has no geometric effect until the feature is unsuppressed. 5. Wrong configuration scope -- setting a value in the wrong configuration silently succeeds for that configuration but does not affect the active view. 6. Post-rebuild failure -- the new value is geometrically valid in isolation but causes a downstream feature to fail (e.g., a cut that now exceeds the body bounds). The rebuild completes but the model has errors visible in diagnostics. |
| **Verification method** | Re-read the dimension value via `Dimension.GetUserValueIn(model)` and compare to the intended value. Check `IModelDocExtension.NeedsRebuild2` to confirm rebuild completed. Check `IModelDoc2.GetFirstModelDoc2Error()` or feature error states for downstream failures. |
| **Recommendation** | **Wave 2 -- Tier 1 (safe for first-wave automation).** ActionClass: Yellow. Requires preview, confirmation, undo grouping, rebuild, and verification. Pre-check driven state. Pre-check suppression state. |

---

### 2.2 Custom Property Set/Add/Delete

| Attribute | Detail |
|-----------|--------|
| **Operation** | Add, modify, or delete a custom property at the document or configuration level |
| **API entry points** | `CustomPropertyManager.Add3(name, type, value, overwriteOption)`, `CustomPropertyManager.Set2(name, value)`, `CustomPropertyManager.Delete2(name)`. Manager obtained via `IModelDocExtension.get_CustomPropertyManager("")` (document-level) or `get_CustomPropertyManager("ConfigName")` (configuration-level). |
| **Grouped undo supported** | **Yes.** Custom property operations are recorded in the undo stack. Multiple property changes within a single undo group appear as one undo step. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` after a grouped property change restores the prior property state exactly. For `Add3`, undo removes the property. For `Set2`, undo restores the prior value. For `Delete2`, undo restores the deleted property with its prior value and type. |
| **Before/after snapshot needed** | **Recommended.** Read the property value via `CustomPropertyManager.Get6()` before and after. Especially important for `Delete2`, where the trace log should record the deleted value for audit purposes. |
| **Rebuild required** | **No.** Custom properties are metadata. No geometric rebuild is needed. |
| **Failure modes** | 1. `Add3` with `swCustomPropertyOnlyIfNew` returns `swCustomInfoAddResult_AlreadyExists` if the property exists. No state change. 2. `Set2` on a non-existent property may silently fail (returns error code, no exception). 3. `Delete2` on a non-existent property returns a failure code but does not throw. 4. Overwriting a **linked property** (e.g., one whose value expression is `"$PRP:SW-File Name"`) replaces the expression with a literal string, breaking the link. The prior expression is not recoverable without a snapshot or undo. 5. Setting a property on a configuration that does not exist silently creates a configuration-level property manager but may not behave as expected. Verify configuration existence first. |
| **Verification method** | Re-read the property via `CustomPropertyManager.Get6()` and compare to the intended value. For deletions, confirm the property is absent. |
| **Recommendation** | **Wave 2 -- Tier 1 (safest first-wave automation).** ActionClass: Yellow. Lowest risk of all write operations. No rebuild, no geometry impact, fully undoable, clean return codes. Must warn before overwriting linked properties. |

---

### 2.3 Feature Suppression State Change

| Attribute | Detail |
|-----------|--------|
| **Operation** | Suppress or unsuppress a feature |
| **API entry points** | `Feature.SetSuppression2(int state, int inConfig, string[] configNames)`. Feature lookup via `IModelDoc2.FeatureByName(name)` or traversal. State enum: `swFeatureSuppressionAction_e`. |
| **Grouped undo supported** | **Yes.** Suppression changes, including cascading child suppression, are captured in a single undo group when wrapped in `StartRecordingUndoObject` / `FinishRecordingUndoObject`. |
| **Reversal deterministic** | **Mostly yes, with an important asymmetry.** Suppressing a parent automatically suppresses its children. Undoing the suppression restores the parent to its unsuppressed state, **and also restores the children to their prior state.** The undo stack correctly tracks the cascade. However: if you suppress a parent (which cascades to children), and then **separately** unsuppress one child, and then undo the unsuppression, you are back to parent-suppressed + all-children-suppressed. The undo stack is linear, not tree-structured, so interleaved operations on parents and children undo in stack order. |
| **Before/after snapshot needed** | **Strongly recommended.** Suppression cascades make the effective change larger than the explicit request. Adze should snapshot the suppression state of the target feature **and all its children and dependents** before applying, and compare after. This enables accurate trace logging and cascade-aware verification. |
| **Rebuild required** | **Yes.** `EditRebuild3()` must be called after suppression state changes for geometry to update. |
| **Failure modes** | 1. `SetSuppression2` returns `false` if the feature is in an edit state (e.g., sketch edit mode is active). No state change. 2. Suppressing a feature that other features reference externally (e.g., in-context references from another part in an assembly) does not fail, but breaks those external references. The referencing documents will show rebuild errors. 3. Unsuppressing a feature whose parent is suppressed fails silently -- the feature cannot resolve without its parent. 4. Suppressing/unsuppressing in a read-only configuration fails silently. 5. Suppressing a sketch that is the basis for a boss extrude cascades to the extrude. This is correct behavior but can surprise users. |
| **Verification method** | Re-read `Feature.IsSuppressed2()` for the target feature. Also check suppression state of known dependents. Check `IModelDocExtension.NeedsRebuild2` after rebuild. |
| **Recommendation** | **Wave 2 -- Tier 2 (safe with dependency awareness).** ActionClass: Yellow. Requires pre-checking the feature dependency graph (`Feature.GetParents()`, `Feature.GetChildren()`) and presenting the cascade in the preview. |

---

### 2.4 Configuration-Specific Edits (Dimension Values and Suppression)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Set a dimension value or suppression state in a specific named configuration (not the active one) |
| **API entry points** | `Dimension.SetSystemValue3(value, swSetValue_InSpecifiedConfigurations, configNames)` for dimensions. `Feature.SetSuppression2(state, swSpecifyConfiguration, configNames)` for suppression. |
| **Grouped undo supported** | **Yes.** Configuration-scoped changes are recorded in the undo stack and can be grouped. |
| **Reversal deterministic** | **Yes, but invisible until the configuration is activated.** Undoing a change made to a non-active configuration restores that configuration's stored values, but the user sees no visual change in the viewport because they are viewing a different configuration. This is correct but disorienting. |
| **Before/after snapshot needed** | **Required.** Because the change is invisible in the current viewport, verification must explicitly activate the target configuration or read the configuration-specific value without switching. For dimensions: `Dimension.GetSystemValue3(swInConfigurationOpts_e.swSpecifyConfiguration, configNames)`. For suppression: `Feature.IsSuppressed2(swSpecifyConfiguration, configNames)`. |
| **Rebuild required** | **Only if the target configuration is active.** Changes to non-active configurations take effect when those configurations are activated. |
| **Failure modes** | 1. Specifying a configuration name that does not exist causes the operation to fail silently (no exception, but the value is not set). 2. Setting a dimension in all configurations (`swSetValue_InAllConfigurations`) overwrites configuration-specific overrides, which may not be the user's intent. 3. The user may not realize a change was made to a non-active configuration, leading to confusion later. |
| **Verification method** | Read the value back using the configuration-specific accessor. Do not rely on visual inspection of the viewport. |
| **Recommendation** | **Wave 2 -- Tier 2, but only for the active configuration initially.** Writing to non-active configurations should require explicit user confirmation with a warning that the change is not visible in the current viewport. ActionClass: Yellow for active-config writes, elevated confirmation for cross-config writes. |

---

### 2.5 Sketch Creation

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a new 2D sketch on a plane or face, add geometry (lines, arcs, circles, rectangles), optionally add constraints, and exit sketch edit mode |
| **API entry points** | `IModelDoc2.Extension.SelectByID2()` (select plane/face), `SketchManager.InsertSketch(true)` (enter sketch mode), `SketchManager.CreateLine()`, `CreateCircle()`, `CreateArc()`, `CreateCornerRectangle()`, `IModelDoc2.SketchAddConstraints()`, `SketchManager.InsertSketch(true)` again (exit sketch mode). 3D sketches: `SketchManager.Insert3DSketch(true)`. |
| **Grouped undo supported** | **Partially.** The entire sketch creation (enter, add geometry, exit) can be wrapped in `StartRecordingUndoObject` / `FinishRecordingUndoObject`, and it will appear as a single undo step. However, if an exception or crash occurs **while in sketch edit mode**, `FinishRecordingUndoObject` in the `finally` block will close the undo group, but the model may still be in sketch edit mode. The undo group will contain a partial sketch. |
| **Reversal deterministic** | **Conditionally.** If the sketch is created, populated, and exited cleanly, `EditUndo2(1)` removes the entire sketch and restores the prior feature tree state. This is deterministic. If the sketch was only partially created (entered but not exited), the undo behavior is unreliable -- the model may remain in sketch edit mode after undo, or the sketch may be partially removed. |
| **Before/after snapshot needed** | **Required.** Capture the feature tree before and after to confirm exactly one new sketch feature was added. Also capture the active edit state (`IModelDoc2.GetActiveSketch2()`) to confirm the model is not still in sketch edit mode. |
| **Rebuild required** | **Implicitly.** Exiting sketch edit mode via `InsertSketch(true)` triggers a partial rebuild. No explicit `EditRebuild3()` is needed for the sketch itself, but one may be needed if the sketch will be consumed by a feature. |
| **Failure modes** | 1. **Orphaned sketch edit mode** -- the most dangerous failure. If the add-in crashes, throws an exception, or loses context while in sketch edit mode, the model is left in sketch edit mode. The user cannot perform normal model operations until they manually exit the sketch. The add-in must detect this state on re-entry via `IModelDoc2.GetActiveSketch2()` and exit it if found. 2. No plane/face selected before `InsertSketch(true)` -- the method may open a sketch on an arbitrary default plane or fail silently. 3. Selecting a suppressed plane -- `SelectByID2` returns `false`, but `InsertSketch` may still execute on whatever was previously selected. 4. Creating geometry with invalid coordinates (e.g., zero-length line) -- the segment is not created, but no exception is thrown. 5. Creating geometry in the wrong coordinate system -- all sketch coordinates are in meters regardless of document units. Unit mismatches produce incorrectly scaled geometry. 6. Sketch constraints on incompatible entities fail silently. |
| **Verification method** | Check `IModelDoc2.GetActiveSketch2() == null` to confirm sketch edit mode was exited. Compare the feature tree before and after to find the new sketch feature. Read the sketch's segment count via `Sketch.GetSketchSegments()`. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The modal state risk (orphaned sketch edit mode) makes this operation fundamentally more dangerous than value-setting operations. Requires robust state detection and recovery. Should not be attempted until value-setting tools are proven and traced. |

---

### 2.6 Feature Creation (Extrusion)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a boss-extrude or cut-extrude feature from an existing sketch profile |
| **API entry points** | `FeatureManager.FeatureExtrusion3(...)` (24 parameters) for boss extrude. `FeatureManager.FeatureCut4(...)` (28 parameters) for cut extrude. Requires a valid closed sketch profile to be pre-selected or active. |
| **Grouped undo supported** | **Yes.** Feature creation is recorded in the undo stack. It can be included in an undo group. The implicit rebuild triggered by feature creation is absorbed into the group. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the created feature and restores the prior feature tree and geometry. The underlying sketch is preserved (features consume sketches, not destroy them). |
| **Before/after snapshot needed** | **Required.** Feature creation has so many parameters that verification must confirm the correct feature type was created with the correct depth, direction, and end condition. Capture the feature tree before and after. Read the new feature's parameters via `Feature.GetDefinition()` to confirm they match intent. |
| **Rebuild required** | **Implicitly.** Feature creation methods trigger a rebuild internally. |
| **Failure modes** | 1. `FeatureExtrusion3` returns `null` if the operation fails. No exception is thrown. Common causes: no valid sketch profile selected, open (non-closed) contour for boss extrude, sketch on a suppressed plane. 2. Incorrect parameter combinations produce unexpected geometry silently. The 24-parameter signature of `FeatureExtrusion3` is error-prone. A wrong boolean for draft direction or a swapped depth value creates valid but incorrect geometry. 3. A sketch profile with multiple closed contours may extrude all of them, which can be surprising. 4. Thin-feature parameters (parameters 20-24) create thin-walled extrusions when non-zero; accidentally setting them produces unexpected geometry. 5. Merge-result flag (parameter 18) controls whether the extrusion merges with existing bodies or creates a new body. Wrong setting causes multi-body parts or unexpected Boolean operations. |
| **Verification method** | Confirm `FeatureExtrusion3` returned a non-null `Feature`. Read the feature's type name. Read the depth dimension value. Check rebuild state for errors. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The parameter complexity, prerequisite state (valid sketch), and risk of silently incorrect geometry make this unsuitable for early automation. Even with correct undo support, the cost of debugging incorrect feature creation is high. |

---

### 2.7 Mate Creation (Assembly)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Add a mate between two entities in an assembly |
| **API entry points** | `IAssemblyDoc.AddMate5(int mateType, int align, bool flip, double distance, double upperLimit, double lowerLimit, double gearRatio1, double gearRatio2, double angle, double angleUpper, double angleLower, bool forPositioningOnly, bool lockRotation, int widthOption, out int errorStatus)`. Requires exactly two entities pre-selected with correct selection marks. |
| **Grouped undo supported** | **Yes.** Mate creation is recorded in the undo stack and can be wrapped in an undo group. |
| **Reversal deterministic** | **Mostly yes.** `EditUndo2(1)` removes the mate and restores the prior assembly position. However, if the mate caused SOLIDWORKS to move components to satisfy the constraint, undoing the mate moves components back to their pre-mate positions. If the user manually repositioned components after the mate was added, and then undo is called, the manual repositioning is also undone (standard stack-order behavior). |
| **Before/after snapshot needed** | **Required.** Capture the mate list and component positions before and after. Mate creation can move components in non-obvious ways, and the user needs to understand what changed. |
| **Rebuild required** | **Yes.** `IAssemblyDoc.EditRebuild3()` is needed to see positional effects. |
| **Failure modes** | 1. **Over-constraint.** Adding a mate that conflicts with existing mates produces an error in `errorStatus` (`swAddMateError_e`) but may partially apply. The mate may appear in the feature tree with an error icon. Undoing it is safe. 2. **Wrong selection.** `AddMate5` requires exactly two entities pre-selected. If the selection is wrong (wrong entity count, wrong entity types), the method fails and sets `errorStatus`. 3. **Selection mark issues.** Some mate types require entities to be selected with specific selection marks (`SelectByID2` mark parameter). Incorrect marks cause mate creation to fail. 4. **Component moved unexpectedly.** A valid mate may move components to positions the user did not anticipate. The mate itself is correct, but the geometric result is surprising. 5. **Redundant mate.** Adding a mate that is already implied by existing mates produces a redundant mate warning. The mate is created but flagged. |
| **Verification method** | Check `errorStatus` output parameter. Read the mate list to confirm the new mate exists. Check for over-defined/redundant warnings via feature state. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The selection-dependent, two-entity prerequisite makes this harder to automate reliably than single-entity operations. Over-constraint risk requires deep mate graph understanding. |

---

### 2.8 Component Insertion (Assembly)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Insert a part or sub-assembly into an assembly at a specified position |
| **API entry points** | `IAssemblyDoc.AddComponent5(string path, int resolveState, string configName, bool newInstance, string configOption, double x, double y, double z)`. |
| **Grouped undo supported** | **Yes.** Component insertion is recorded in the undo stack. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the inserted component and restores the prior assembly state. No side effects on other components. |
| **Before/after snapshot needed** | **Recommended.** Capture the component list before and after. The insertion itself is straightforward, but the component path must be validated beforehand. |
| **Rebuild required** | **Implicitly.** Component insertion triggers an automatic lightweight rebuild. |
| **Failure modes** | 1. Invalid `compPath` (file does not exist) -- `AddComponent5` returns `null`. No state change. 2. Component file is read-only or locked by another process -- may load as read-only or fail to load. 3. Configuration name does not exist in the referenced file -- component loads in default configuration. 4. Insertion coordinates are far from the assembly origin -- component is placed at an unexpected location. 5. Circular reference -- inserting an assembly into itself or a descendant of itself. SOLIDWORKS blocks this. |
| **Verification method** | Confirm `AddComponent5` returned a non-null `Component2`. Read the component list to verify the new component exists. |
| **Recommendation** | **Wave 3 (after Tier 1/2 tools are proven).** ActionClass: Yellow if the component path is user-confirmed, Red if auto-selected. The operation itself is clean and undoable, but file system dependencies add failure surface. |

---

### 2.9 Drawing View Creation

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a standard, projected, section, or detail view in a drawing document |
| **API entry points** | `IDrawingDoc.CreateDrawViewFromModelView3(string modelPath, string viewName, double x, double y, double z)` for named model views. `IDrawingDoc.InsertModelView3(string modelPath, int viewOrientation, double x, double y, double z, int displayMode, string configName)` for standard orientation views. Projected views: `IDrawingDoc.CreateProjectedView()` (requires a base view selected). Section views: `IDrawingDoc.CreateSectionViewAt5(...)`. Detail views: `IDrawingDoc.CreateDetailViewAt4(...)`. |
| **Grouped undo supported** | **Yes.** Drawing view creation is recorded in the undo stack and can be grouped. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the created view and restores the prior drawing sheet state. |
| **Before/after snapshot needed** | **Required.** Capture the drawing sheet's view list before and after. Drawing views reference external model files, so their creation depends on file availability and model state. |
| **Rebuild required** | **Implicitly.** View creation triggers an automatic rebuild/view generation. |
| **Failure modes** | 1. `modelPath` does not exist or is not loadable -- returns `null`. 2. The referenced model has rebuild errors -- the view may show with error indicators. 3. Incorrect position coordinates place the view off-sheet or overlapping other views. SOLIDWORKS does not prevent overlapping views. 4. Section views and detail views have additional prerequisites (a base view must exist and a section line or detail circle must be defined). 5. Creating a view on the wrong sheet (if the drawing has multiple sheets). |
| **Verification method** | Confirm the method returned a non-null `View`. Read the sheet's view list. Check view position and scale. |
| **Recommendation** | **Wave 3 (planned in BUILD_SPEC.md as Wave 3 tool).** ActionClass: Yellow for basic standard views from a confirmed model path. Red for section/detail views due to prerequisite complexity. |

---

### 2.10 Object Rename

| Attribute | Detail |
|-----------|--------|
| **Operation** | Rename a feature, component, configuration, or other named entity |
| **API entry points** | `Feature.Name = "NewName"` (direct property set for features). `Component2.Name2 = "NewName"` for components. `Configuration.Name = "NewName"` for configurations. |
| **Grouped undo supported** | **Yes.** Rename operations are recorded in the undo stack. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` restores the prior name. |
| **Before/after snapshot needed** | **Recommended.** Record old name and new name for trace logging. |
| **Rebuild required** | **No.** Renames are metadata operations (but note: renaming a feature that is referenced by name in equations or macros may break those references, and this breakage is not detected until the next rebuild or equation evaluation). |
| **Failure modes** | 1. Duplicate name -- SOLIDWORKS may auto-append a suffix or reject the rename. Behavior varies by entity type. 2. Empty or whitespace name -- behavior varies. Some entities accept empty names, others reject them. 3. Renaming a feature that is referenced in equations by name (e.g., `"D1@OldName"`) breaks the equation reference. The equation continues to reference the old name string, which no longer resolves. This is not detected until the equation is evaluated. 4. Renaming a component in an assembly that is referenced by in-context features in other components may break those references. |
| **Verification method** | Re-read the entity's name property and compare. |
| **Recommendation** | **Wave 2 -- Tier 1 (safe for first-wave automation).** ActionClass: Yellow. Simple, fully undoable, no rebuild. Must warn about equation/reference impacts. |

---

### 2.11 Entity Selection / Highlighting

| Attribute | Detail |
|-----------|--------|
| **Operation** | Select or highlight entities in the model (features, faces, edges, dimensions) for user attention |
| **API entry points** | `IModelDocExtension.SelectByID2(name, type, x, y, z, append, mark, callout, options)`. `IModelDoc2.ClearSelection2(true)`. |
| **Grouped undo supported** | **Not applicable.** Selection changes are **not** recorded in the undo stack. They are transient UI state. |
| **Reversal deterministic** | **Not applicable.** Selection is not an undoable operation. Clearing the selection via `ClearSelection2(true)` is the reversal mechanism. |
| **Before/after snapshot needed** | **Optional.** Selection is transient state. No document modification occurs. |
| **Rebuild required** | **No.** Selection is purely visual/UI state. |
| **Failure modes** | 1. `SelectByID2` returns `false` if the entity is not found. 2. Selecting a suppressed entity returns `false`. 3. Entity names can be ambiguous in assemblies (same feature name in multiple components). The `type` parameter and coordinates help disambiguate. |
| **Verification method** | Read `ISelectionManager.GetSelectedObjectCount2(-1)` and compare to expected count. |
| **Recommendation** | **Wave 2 -- Tier 1 (safest possible operation).** ActionClass: Green. No document modification, no undo needed, no rebuild. This is essentially a read operation with visual side effects. |

---

## 3. Consolidated Safety Matrix

| Operation | Grouped Undo | Reversal Deterministic | Snapshot Needed | Rebuild | Wave | ActionClass | Safe for First-Wave |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Select/highlight entities | N/A | N/A (transient) | Optional | No | 2 | Green | **Yes** |
| Set custom property | Yes | Yes | Recommended | No | 2 | Yellow | **Yes** |
| Rename object | Yes | Yes | Recommended | No | 2 | Yellow | **Yes** |
| Set dimension value | Yes | Yes (with caveats) | Recommended | Yes | 2 | Yellow | **Yes** |
| Suppress feature | Yes | Yes (cascade-aware) | Required | Yes | 2 | Yellow | **Yes** (with dependency preview) |
| Unsuppress feature | Yes | Yes (cascade-aware) | Required | Yes | 2 | Yellow | **Yes** (with dependency preview) |
| Config-specific dimension | Yes | Yes (invisible) | Required | Conditional | 2 | Yellow | **Conditional** (active config only initially) |
| Component insertion | Yes | Yes | Recommended | Implicit | 3 | Yellow/Red | No |
| Drawing view (standard) | Yes | Yes | Required | Implicit | 3 | Yellow | No |
| Sketch creation | Partial | Conditional | Required | Implicit | 3+ | Red | **No** |
| Feature creation | Yes | Yes | Required | Implicit | 3+ | Red | **No** |
| Mate creation | Yes | Mostly | Required | Yes | 3+ | Red | **No** |
| Drawing view (section/detail) | Yes | Yes | Required | Implicit | 3+ | Red | **No** |

---

## 4. Undo Group Contract for Adze

Based on the analysis above, every write tool in Adze should follow this exact undo pattern:

```
1. Pre-check guards
   - Document is not read-only
   - Document is not null
   - Target entity exists and is in a valid state
   - For dimensions: check DrivenState is not driven
   - For suppression: check parent/child cascade

2. Capture before-state
   - Read the value(s) that will change
   - Store in a WriteTrace record

3. Start undo group
   - ext.StartRecordingUndoObject("Adze: " + description)

4. Execute write
   - Call the COM write method
   - Check return code immediately
   - If failed: jump to step 6

5. Rebuild (if required)
   - model.EditRebuild3()
   - Check rebuild state for errors

6. Finish undo group
   - ext.FinishRecordingUndoObject()
   - (MUST be in a finally block)

7. Capture after-state
   - Re-read the value(s) that changed
   - Compare to expected result

8. Emit trace
   - Record before, after, success/failure, undo label
```

### Critical implementation constraints

1. **Never nest undo groups.** `StartRecordingUndoObject` does not support nesting. If a write tool calls another write tool internally, the inner tool must not start its own undo group.

2. **Always close the undo group in a `finally` block.** If `FinishRecordingUndoObject` is not called, the undo stack may be corrupted for the remainder of the session.

3. **Never call `ClearUndoList()`.** This is destructive and irreversible.

4. **Keep undo labels prefixed with `"Adze: "`.** This makes Adze-originated changes identifiable in the Edit > Undo menu.

5. **One undo group per user-confirmed action.** Do not silently chain multiple undo groups. If the user confirms one operation, that is one undo group. If they need to undo, one Ctrl+Z undoes the entire Adze action.

---

## 5. Operations Requiring Elevated Confirmation

The following operations should require elevated confirmation beyond the standard preview-confirm flow, due to higher risk of unintended consequences:

| Operation | Reason for Elevation |
|-----------|---------------------|
| Dimension change that causes downstream rebuild error | The value itself is valid but the geometric consequence is a broken model. Preview should attempt to predict this (hard). |
| Suppression of a feature with external references | Other documents may break. The user may not have those documents open. |
| Custom property overwrite of a linked property | The link expression is destroyed and not easily recoverable. |
| Writing to a non-active configuration | The change is invisible in the current viewport, creating confusion risk. |
| Any write to an assembly document | Assembly writes have broader blast radius than part writes due to inter-component dependencies. |
| Any operation when the document has existing rebuild errors | The model is already in a degraded state; adding writes increases unpredictability. |

---

## 6. Operations That Should Be Excluded from Automation Entirely (Current Phase)

| Operation | Exclusion Reason |
|-----------|-----------------|
| Sketch creation | Modal state risk (orphaned sketch edit mode) is fundamentally different from value-setting risk. Requires dedicated state detection and recovery infrastructure. |
| Feature creation (extrude, cut, revolve, etc.) | Requires sketch creation as a prerequisite. 24-28 parameter signatures make silent mis-parameterization likely. |
| Mate creation | Two-entity selection prerequisite and over-constraint risk require deep assembly graph understanding. |
| Section/detail drawing views | Multi-step prerequisite (base view, section line definition) with modal interaction requirements. |
| Equation modification | Equations form a dependency graph. Modifying one equation can cascade to many dimensions. No clean COM API for equation impact preview. |
| Material assignment | While the API exists (`IPartDoc.SetMaterialPropertyName2`), material changes affect mass properties, simulation results, and BOM entries in ways that are hard to preview. |
| File save (`IModelDoc2.Save3`) | Save is irreversible (cannot undo a save). Must never be triggered automatically without explicit user confirmation. Should be a separate, elevated action, not part of a write tool. |

---

## 7. Snapshot Strategy Recommendations

### What to snapshot before writes

| Write Operation | Snapshot Content |
|----------------|-----------------|
| Dimension change | `{ dimensionFullName, oldValue, units, drivenState, featureName, configurationName }` |
| Custom property | `{ propertyName, scope, configurationName, oldValue, oldType, isLinked, linkExpression }` |
| Suppression change | `{ featureName, oldState, childFeatures[].{name, oldState}, dependentFeatures[].{name, oldState} }` |
| Rename | `{ entityType, oldName, newName, equationReferences[], externalReferences[] }` |

### What to snapshot after writes

| Write Operation | Verification Content |
|----------------|---------------------|
| Dimension change | `{ dimensionFullName, newValue, rebuildState, downstreamErrors[] }` |
| Custom property | `{ propertyName, newValue, newType, isLinked }` |
| Suppression change | `{ featureName, newState, childFeatures[].{name, newState}, rebuildState }` |
| Rename | `{ entityType, newName, resolvedSuccessfully }` |

### Snapshot storage

Snapshots should be stored in the trace event alongside the undo label, enabling post-hoc audit of what Adze changed and whether the change was verified. The trace schema at `schemas/traces/trace-event.schema.json` should be extended with a `write_snapshot` field for this purpose.

---

## 8. Risk Summary and Recommendation

### Safe for Wave 2 (first-wave agent automation)

These operations have clean COM APIs, deterministic undo via grouped undo recording, no modal state risk, and verifiable outcomes:

1. **Select/highlight entities** -- zero risk, no document modification
2. **Set custom property** -- lowest risk write, no rebuild, fully undoable
3. **Rename object** -- simple, fully undoable, no rebuild
4. **Set dimension value** -- moderate risk due to rebuild dependency, but clean API and deterministic undo
5. **Suppress/unsuppress feature** -- moderate risk due to cascade, but clean API, deterministic undo, and verifiable with dependency preview

### Defer to Wave 3+

These operations have modal state risk, complex prerequisites, large parameter surfaces, or inter-document blast radius:

6. **Sketch creation** -- modal state risk
7. **Feature creation** -- prerequisite complexity, parameter surface size
8. **Mate creation** -- two-entity prerequisite, over-constraint risk
9. **Component insertion** -- file system dependency, but relatively clean otherwise
10. **Drawing view creation (standard)** -- external model dependency
11. **Drawing view creation (section/detail)** -- multi-step modal prerequisite

### Exclude entirely until dedicated infrastructure exists

12. **Equation modification** -- dependency graph cascade
13. **Material assignment** -- cross-concern blast radius
14. **File save** -- irreversible, must never be automated without extreme elevation

---

## 9. Open Questions for Implementation

1. **Undo stack depth detection.** Can Adze detect the configured undo stack depth to warn when approaching the limit? The system option `swUserPreferenceIntegerValue_e.swUndoLimit` may provide this. If the stack is nearly full, older Adze undo groups may be silently dropped.

2. **Undo group state detection.** Can Adze detect whether an undo group recording is already active (e.g., started by the user via macro or by another add-in)? There is no known API for this. The safest approach is to always assume no recording is active and to never nest.

3. **Post-rebuild error enumeration.** The existing `NeedsRebuild2` check detects whether a rebuild is needed, but does not enumerate specific errors. For write verification, Adze may need to traverse the feature tree and check each feature's `GetErrorCode2()` to detect downstream failures caused by the write.

4. **Drawing document undo behavior.** Drawing undo behavior for view creation is less well-documented than part/assembly undo. Empirical testing with `StartRecordingUndoObject` on drawing view operations should be performed before implementing Wave 3 drawing tools.

5. **Concurrent add-in writes.** If another add-in modifies the model between Adze's preview and apply steps, Adze's before-snapshot may be stale. There is no COM-level locking mechanism to prevent this. The mitigation is to re-read the value immediately before writing and abort if it differs from the preview.

---

## References

- `discovery-solidworks-write-api.md` -- companion document with full method signatures and parameter enumerations
- `src/Adze.Host/Services/SessionContextBuilder.cs` -- established COM access patterns for read operations
- `src/Adze.Contracts/Enums/ActionClass.cs` -- Green/Yellow/Red action classification
- `src/Adze.Contracts/Enums/ApprovalState.cs` -- Draft through Completed/RolledBack/Failed state machine
- `documentation/BUILD_SPEC.md` -- Wave 2/3 tool lists and safety rules
- `SolidWorks.Interop.sldworks.dll` (R2026x interop)
- `SolidWorks.Interop.swconst.dll` (enumeration definitions)
