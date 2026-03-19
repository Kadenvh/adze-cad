# Research Brief: SOLIDWORKS AI Ecosystem & Competitive Landscape

**Date:** 2026-03-16
**Purpose:** Map the AI tool landscape around SOLIDWORKS/3DEXPERIENCE to inform Adze's positioning and roadmap

---

## 1. Dassault's Virtual Companions: AURA, LEO, MARIE

### AURA (Live Beta)
- **What:** AI-powered virtual companion embedded in the 3DEXPERIENCE platform. Orchestrates knowledge and insight across the platform.
- **Integration:** Lives in the **MySession panel** of the 3DX Task Pane window. Also accessible through 3DSwym conversations (in-browser or in-SOLIDWORKS). **Not a COM add-in** — it's a platform cloud service rendered in the 3DX panel.
- **Capabilities:** Retrieves information from 3DSwym and official Dassault documentation. Summarizes, simplifies, and translates 3DSwym content. Provides access to internal company knowledge stored on 3DEXPERIENCE. Protects IP by staying within the platform boundary.
- **Model/API:** Generative AI (specific model not disclosed). Cloud-hosted on 3DEXPERIENCE infrastructure.
- **Status:** Live on 3DEXPERIENCE platform as of 2025. Available through the 3DSwym app.
- **Scope vs. Adze:** AURA is a **knowledge/documentation assistant**, not a CAD manipulation tool. It answers questions about platform content and documentation. It does NOT inspect the live CAD model, read dimensions, execute tools, or modify geometry.

### LEO (Mid-2026)
- **What:** Engineering-focused virtual companion. Executes commands for troubleshooting and assists with design and simulation.
- **Capabilities:** Engineering feasibility checking, manufacturing constraints, drawing creation assistance (beta: AI-driven drawing creation with template/standards selection and preview).
- **Status:** Launching mid-2026. Drawing creation beta available to 3DEXPERIENCE Connected users.
- **Scope vs. Adze:** LEO is the **most direct competitor** to Adze's write tools. When LEO launches, it will do some of what Adze does (execute design commands, troubleshoot) but through the 3DEXPERIENCE platform layer, not through direct COM access.

### MARIE (Late 2026)
- **What:** Materials science and chemistry focused companion.
- **Scope vs. Adze:** No overlap — completely different domain (materials/formulations).

### Key Insight
Dassault is building a **three-companion architecture** (knowledge / engineering / materials) on top of the 3DEXPERIENCE cloud platform. This requires 3DEXPERIENCE Connected licenses. Desktop-only SOLIDWORKS users without 3DEXPERIENCE connectivity won't have access to these companions.

---

## 2. SOLIDWORKS Labs (Beta) — Native AI Features

### Currently Available Beta Features:
| Feature | What It Does | AI Type |
|---------|-------------|---------|
| **Command Predictor** | Anticipates next SOLIDWORKS command based on session patterns | Predictive ML |
| **Contextual Assistant** | In-context tips and efficiency recommendations | Rule-based + ML |
| **Picture to Sketch** | Converts raster images to sketch entities | Computer vision |
| **Drawing Creation** | AI-assisted drawing template/view selection with LEO | Generative AI |
| **SOLIDWORKS Insight** | Answers SW questions from vetted documentation | RAG/LLM |
| **What's Wrong** | AI-guided root cause analysis for errors | Diagnostic AI |
| **Assembly Structure Design** | Generates assembly structure from description | Generative AI |
| **Mate Helper / Smart Mate** | Anticipates assembly constraints | Predictive ML |
| **Selection Helper** | Smart selection anticipation | Predictive ML |

### Key Insight
These are **built-in features** activated through SOLIDWORKS Labs, not add-ins. They're deeply integrated into the SOLIDWORKS UI. Several overlap with Adze's capabilities (Insight ≈ our documentation search, What's Wrong ≈ our rebuild diagnostics, Command Predictor ≈ our intent clarification). But they're shallow — they don't have Adze's deep grounding through 10+ typed inspection tools.

---

## 3. Third-Party AI Add-ins

### Backflip (Beta)
- **What:** AI-driven mesh-to-CAD conversion. Turns 3D scan data into fully parametric, editable CAD models.
- **Integration:** SOLIDWORKS plug-in (drives SW directly — sketches, extrudes, revolves to match mesh input).
- **Status:** Limited beta, not publicly available.
- **Overlap with Adze:** None — completely different domain (reverse engineering).
- **Lesson:** Demonstrates that third-party AI add-ins that directly drive SOLIDWORKS COM are viable and accepted by the community.

### Leo AI (getleo.ai)
- **What:** Enterprise knowledge management AI for SOLIDWORKS. Preserves tribal knowledge, prevents design mistakes.
- **Integration:** Integrates into SOLIDWORKS environment, learns from company design history and PDM data.
- **Customers:** HP, Intel, Philips. Claims $1.3M annual savings.
- **Overlap with Adze:** Moderate — Leo focuses on **cross-session knowledge** (PDM, design history, tribal knowledge) while Adze focuses on **live session grounding** (current model, dimensions, features). Complementary more than competitive.
- **Lesson:** Enterprise knowledge management is a proven value prop. Adze's per-document memory and recipe system is a lighter version of this.

### MecAgent
- **What:** AI CAD copilot for SolidWorks, CATIA, and other platforms. Standards compliance, cost estimation, model generation from text/sketches.
- **Integration:** Plugin integration with major CAD platforms.
- **Overlap with Adze:** Significant — MecAgent is closest to Adze's vision of an in-CAD AI assistant. But MecAgent appears to be more of an external wrapper while Adze is a native COM add-in.
- **Lesson:** Multi-CAD support matters for market reach. Adze is SOLIDWORKS-only, which is both a strength (deeper integration) and a limitation.

---

## 4. Competitive Landscape — Other CAD AI Assistants

### Autodesk Assistant (Fusion 360)
- **Integration:** Built natively into Autodesk Platform. Available across Fusion, AutoCAD, Revit.
- **Capabilities:** Conversational AI, prompt-based BOM/property updates, AI-assisted onboarding, text-to-3D geometry generation (native editable geometry, not mesh). **MCP integration** announced — customers can build agent-driven workflows.
- **2026 Roadmap:** Extensibility through MCP servers, AI-powered part duplication prevention, expanded automation.
- **Key Lesson for Adze:** Autodesk is embracing **MCP (Model Context Protocol)** for extensibility. This is a significant architectural signal — they're making their AI assistant a platform that third parties can extend.

### Siemens NX Copilot / Solid Edge Copilot
- **NX Copilot:** Translates natural language into domain-specific commands. Helps with design approaches, error resolution, workflow streamlining.
- **Solid Edge 2026 Copilot:** In-app AI chat for product guidance and support. Searches Siemens documentation, tutorials, and training libraries.
- **AI-Powered Auto-Drawings:** Generates 2D documentation that's up to 80% complete automatically (intelligent view placement, dimensioning, template selection).
- **Key Lesson for Adze:** Siemens focuses on "natural language → action" translation and auto-documentation. Their copilots are chat-based with deep documentation RAG.

### Onshape AI Advisor
- **What:** Contextual advice based on best practices and Onshape documentation.
- **Integration:** Built into Onshape's cloud-native platform.
- **Key Lesson:** Cloud-native CAD has architectural advantages for AI (easier data access, no COM threading issues).

### PTC / Creo
- No prominent AI assistant yet. Focus remains on simulation and manufacturing optimization.

---

## 5. Strategic Analysis

### Is Dassault making Adze redundant?

**Short answer: Not yet, and probably not for desktop-only users.**

- AURA is a **knowledge assistant** (documentation/3DSwym search), not a CAD tool assistant. It doesn't read the live model.
- LEO (mid-2026) is the real competitor, but it requires **3DEXPERIENCE Connected** licenses.
- A large installed base of SOLIDWORKS users run **desktop-only** without 3DEXPERIENCE connectivity. These users get zero AI assistants from Dassault.
- Even with 3DEXPERIENCE, Dassault's companions operate through the **platform cloud layer**, not through direct COM access. Adze's grounding depth (typed tool results from live COM inspection) is architecturally different and potentially richer.

### Positioning Recommendation

**Adze should position as: "Deep grounding for desktop SOLIDWORKS users who don't have or don't want 3DEXPERIENCE cloud dependencies."**

Key differentiators:
1. **Works without 3DEXPERIENCE** — desktop-only, no cloud dependency
2. **Deep live model grounding** — 10 typed inspection tools + 4 write tools with full lifecycle
3. **User-controlled AI provider** — bring your own API key (OpenAI, Anthropic, OpenRouter, local models)
4. **Governed writes** — preview/apply/verify/rollback with trust tiers, not black-box AI
5. **Offline-capable** — deterministic fallback when no model available

### Platform API Opportunities

| API/Service | Potential | Notes |
|-------------|-----------|-------|
| 3DSwym API | Medium | Could index community knowledge if user has 3DX license |
| ENOVIA/3DSpace | Medium | Bill of materials, lifecycle state, revision history |
| Document Manager API | High | Already planned (T6-05) — enriches closed-file indexing |
| SOLIDWORKS API (COM) | Already using | Core integration path |
| MCP Protocol | Watch | Autodesk's adoption signals industry direction |

### Features to Consider Adopting

1. **Auto-drawing assistance** (Siemens/Dassault both doing this) — would require Phase 7+ write tools
2. **Command prediction** (SOLIDWORKS Labs has this) — Adze could predict based on session context + recipes
3. **Text-to-geometry** (Autodesk doing this) — far future, but the architectural foundation exists
4. **MCP server exposure** — let external agents connect to Adze's grounding tools
5. **Enterprise knowledge integration** (Leo AI's approach) — Adze's recipe/memory system is a foundation for this

### What to Avoid

1. Don't compete on documentation search — AURA and SOLIDWORKS Insight already do this well
2. Don't require cloud connectivity — this is Adze's key differentiator
3. Don't try to be multi-CAD — depth > breadth at this stage
4. Don't replicate Labs beta features that will become native — focus on what Dassault can't do (user-chosen models, governed writes, offline operation)

---

## Sources

- [Dassault AURA Beta Announcement](https://3dswym.3dexperience.3ds.com/wiki/solidworks-news-info/new-ai-companion-aura-beta_wXE6lvKQRRWkYDTVxfCl8w)
- [What Is AURA? - CADimensions](https://resources.cadimensions.com/cadimensions-resources/aura-your-new-personal-design-assistant-in-solidworks-0)
- [SOLIDWORKS and AI in 2026 - SOLIDX](https://www.solidx.co.uk/resources/post/solidworks-and-ai-in-2026/)
- [AI Buzz in SOLIDWORKS - Hawk Ridge](https://hawkridgesys.com/blog/what-is-here-today-ai-in-solidworks-cad-3dexperience)
- [New SW AI Agents at 3DX World - DEVELOP3D](https://develop3d.com/cad/new-solidworks-ai-agents-added-at-3dexperience-world/)
- [Dassault Virtual Companions Strategy](https://www.enterprisetimes.co.uk/2026/02/04/dassault-systemes-unveils-virtual-companions-and-industrial-ai-strategy-at-3dexperience-world-2026/)
- [SOLIDWORKS AURA - SwiftSol](https://www.swyftsol.com/blog/solidworks-aura-ai-powered-context-and-design-assistant-for-engineering-teams)
- [10 AI Tools Coming to SOLIDWORKS 2026 - Engineering.com](https://www.engineering.com/10-ai-tools-coming-to-solidworks-in-2026/)
- [3 AI Features Coming to Every CAD Program in 2026 - Engineering.com](https://www.engineering.com/3-ai-features-coming-to-every-cad-program-in-2026/)
- [AI in SOLIDWORKS: What It Is - GoEngineer](https://www.goengineer.com/blog/ai-in-solidworks)
- [Autodesk Assistant AI - Fusion Blog](https://www.autodesk.com/products/fusion-360/blog/autodesk-assistant-ai/)
- [Fusion Roadmap 2026](https://www.autodesk.com/products/fusion-360/blog/fusion-roadmap-2026/)
- [Autodesk Fusion MCP Integration](https://glama.ai/mcp/servers/@sockcymbal/autodesk-fusion-mcp-python)
- [Siemens NX AI Copilot](https://news.siemens.com/en-us/siemens-designcenter-nx-summer-2025/)
- [Solid Edge 2026 AI Copilot](https://www.engineering.com/siemens-launches-solid-edge-2026-with-ai-design-copilot/)
- [Solid Edge 2026 AI Features](https://blogs.sw.siemens.com/solidedge/designcenter-solid-edge-2026-artificial-intelligence/)
- [Backflip Mesh-to-CAD Plugin](https://mechnexus.com/backflips-new-ai-based-plug-in-for-solidworks/)
- [Leo AI for SOLIDWORKS](https://www.getleo.ai/blog/leo-ai-solidworks-enterprise-knowledge-management)
- [MecAgent AI CAD Copilot](https://mecagent.com/)
- [3DEXPERIENCE World 2026 AI Strategy - Dassault](https://www.3ds.com/newsroom/press-releases/ai-forefront-creation-and-innovation-exploring-future-design-and-manufacturing-dassault-systemes-3dexperience-world-2026)
- [AI Can Now Use SOLIDWORKS - Engineering.com](https://www.engineering.com/ai-can-now-use-solidworks/)
- [Dassault AI Virtual Companions Launch](https://roboticsandautomationnews.com/2026/03/11/dassault-systemes-unveils-new-way-of-working-with-ai-powered-virtual-companions/99480/)
