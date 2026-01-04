---
description: STRICT PROTOCOL for writing and verifying code. MUST FOLLOW for every coding task.
---

# AGENT RIGOROUS VERIFICATION PROTOCOL

## 🛑 STOP & CHECK BEFORE CODING

Before writing a single line of code, you **MUST** perform the following context checks. **Do not assume anything.**

### 1. Context & Availability Check
*   **Imports**: Do I see the file where `using` or `import` statements are defined?
*   **Variables**: If I use a variable `x`, have I verified its definition in the *current* scope or its class file?
*   **Functions**: If I call `FunctionY()`, have I seen its signature? Do I know its return type and parameters?
    *   *Action*: Use `grep_search` or `view_code_item` to confirm existence if unsure.
*   **Dependencies**: If using a library (e.g., `Newtonsoft.Json`), is it in the `.csproj` or `package.json`?

### 2. Impact Analysis
*   **Upstream/Downstream**: Who calls this function? Will changing the return type break them?
*   **State Consistency**: Does this change rely on global state that might not be initialized?

### 3. Logic & Holistic Flow Analysis (The "Why It Might Fail" Check)
*   **Bounds & Geometry**: If moving/clicking things, am I respecting the *container's* bounds? (e.g., Is the submenu wider than the main menu hit-test?)
*   **Temporal Race Conditions**:
    *   Am I relying on an animation to finish?
    *   Am I sending input (keys/clicks) faster than the OS/App can switch focus?
    *   *Rule*: Always add safety buffers (delays) when switching contexts.
*   **Visual vs. Logical State**: confirm that what the user *sees* (e.g., hover highlight) matches the *math* used for the click. Don't re-calculate if you already have the state.

---

## 🛠️ DURING CODING (Rules of Engagement)

1.  **No Blind Coding**: Never call a function you haven't confirmed exists.
2.  **Complete Snippets**: When replacing code, ensure the replacement is syntactically complete. Do not leave "..." unless explicitly skipping irrelevant blocks (and using a valid tool for it).
3.  **Typos**: Double-check variable names against their definitions.

---

## ✅ POST-CODING VERIFICATION (Mandatory)

After applying changes, you **MUST** verify them. "I think it works" is NOT acceptable.

### 1. Compilation Verification
**Run the build command immediately.**
```powershell
dotnet build
```
*   **If Build Fails**:
    *   **DO NOT** ask the user what to do.
    *   **READ** the error message carefully.
    *   **FIX** it immediately.
    *   **RE-RUN** `dotnet build` until it passes.

### 2. Runtime Verification (If possible)
*   If a test suite exists, run it: `dotnet test`
*   If no tests exist, review the logic again: "Did I handle null cases? Did I dispose of resources?"

### 3. Final sanity check
*   Did I break the build?
*   Did I leave any debug print statements?

---

**CRITICAL**: If you encounter an error you cannot fix after 2 attempts, **THEN** verify with the user, providing the *exact* error log and what you tried.
