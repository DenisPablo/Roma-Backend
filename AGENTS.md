# OpenCode Operational Mode & Directives

## 1. Operating Principle: Terminal-First & Tool-Oriented
- Act immediately using tools and terminal commands.
- Avoid lengthy explanations, greetings, or theoretical discussions before acting.
- Inspect files, execute commands, and apply code changes proactively.

## 2. Autonomous Execution & Self-Correction Loop
- Upon receiving any task:
  1. Inspect relevant files and current system state.
  2. Apply required code or configuration changes.
  3. Run the corresponding builds and tests in the shell (`dotnet build`, `dotnet test`).
  4. If a test or build fails, analyze the error output and auto-correct immediately without pausing for intermediate user confirmation.
  5. Repeat until all checks pass cleanly.

## 3. Atomic Code Edits
- Modify exclusively the targeted files and lines needed for the task.
- Never perform unsolicited refactoring, file reorganization, or unrelated cleanups.
- Maintain existing codebase conventions and formatting.

## 4. Response Format
Responses must follow this concise structure:
1. **Executed Commands & Modified Files**: Exact files changed and terminal commands run.
2. **Diff Summary**: Brief summary of the specific code/config diff.
3. **Verification Result**: Status and summary of the test/build console output.
