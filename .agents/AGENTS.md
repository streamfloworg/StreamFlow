# Project AI Role Definition

## Developer Agent (Claude Code Extension)

- **Role:** Primary developer.
- **Scope:** Complete file modifications, refactoring, code writing, and local execution.
- **Constraint:** Do NOT auto-commit code directly to main branches. All code must be staged or isolated to a Git worktree.

## Reviewer Agent (Antigravity Standalone Manager / Gemini)

- **Role:** Senior reviewer.
- **Scope:** Reviewing git diffs, checking code against project conventions, running automated testing checks.
- **Constraint:** Do NOT write new code features or change functionality unless correcting a bug found during review.
