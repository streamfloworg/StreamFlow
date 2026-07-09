---
trigger: always_on
---

1. Never use npm install to install packages. pnpm install or pnpm add must be used.
2. To start the app, ALWAYS use pnpm start if not running a debugging session. vite or npx for other purposes is fine.
3. No architecural changes allowed without approval.
4. Track all changes that are not single file changes with TODO lists/Tasks lists to ensure changes are not lost.
5. Do not over-complicate the code.
6. No overuse of logging (i.e. console.log, console.debug, etc)
7. Performance is always priority, unless it significantly sacrifices quality.
8. Post edit build tests are required. tsx, vite, lint, whatever is relevant for the code changes that were performed.
9. The UI is Electron, which means it should not do any heavy lifting if Core is capable. Offload expensive processing to the Rust core whenever possible.
