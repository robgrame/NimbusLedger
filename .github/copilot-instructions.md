*Use this file to provide workspace-specific custom instructions to Copilot.*
- [x] Verify that the copilot-instructions.md file in the .github directory is created.

- [x] Clarify Project Requirements
	*Ask for project type, language, and frameworks if not specified. Skip if already provided.*

- [x] Scaffold the Project
	- Ensure that the previous step has been marked as completed.
	- Call project setup tool with projectType parameter.
	- Run scaffolding command to create project files and folders.
	- Use '.' as the working directory.
	- If no appropriate projectType is available, search documentation using available tools.
	- Otherwise, create the project structure manually using available file creation tools.

- [x] Customize the Project
	- Verify that all previous steps have been completed successfully and you have marked the step as completed.
	- Develop a plan to modify the codebase according to user requirements.
	- Apply modifications using appropriate tools and user-provided references.
	- Skip this step for "Hello World" projects.

- [x] Install Required Extensions
	*Only install extensions provided by get_project_setup_info. Skip this step otherwise and mark as completed.*

- [x] Compile the Project
	- Verify that all previous steps have been completed.
	- Install any missing dependencies.
	- Run diagnostics and resolve any issues.
	- Check for markdown files in the project folder for relevant instructions on how to do this.

- [x] Create and Run Task
	- Verify that all previous steps have been completed.
	- Review https://code.visualstudio.com/docs/debugtest/tasks to determine if the project needs a task.
	- When needed, create and launch a task based on `package.json`, `README.md`, and the project structure.
	- Skip this step if tasks are unnecessary.

- [ ] Launch the Project
	*Verify that all previous steps have been completed. Prompt the user for debug mode and launch only if confirmed.*

- [ ] Ensure Documentation is Complete
	- Verify that all previous steps have been completed.
	- Confirm that `README.md` and `.github/copilot-instructions.md` contain current project information.
	- Remove any lingering HTML comments from this file.

## Execution Guidelines

**Progress tracking**
- If any tools are available to manage the above todo list, use them to track progress through this checklist.
- After completing each step, mark it complete and add a summary.
- Read the current todo list status before starting each new step.

**Communication rules**
- Avoid verbose explanations or printing full command outputs.
- If a step is skipped, state that briefly (for example, "No extensions needed").
- Do not explain the project structure unless asked.
- Keep explanations concise and focused.

**Development rules**
- Use `.` as the working directory unless the user specifies otherwise.
- Avoid adding media or external links unless explicitly requested.
- Use placeholders only with a note that they should be replaced.
- Use the VS Code API tool only for VS Code extension projects.
- Once the project is created, assume it is already open in Visual Studio Code—do not suggest commands to open it again in Visual Studio.
- If the project setup information has additional rules, follow them strictly.

**Folder creation rules**
- Always use the current directory as the project root.
- When running terminal commands, use the `.` argument to ensure the current working directory is used.
- Do not create a new folder unless the user explicitly requests it (besides a `.vscode` folder for a `tasks.json` file).
- If scaffolding commands complain about folder names, inform the user so they can create the correct folder and reopen it in VS Code.

**Extension installation rules**
- Only install extensions specified by `get_project_setup_info`. Do not install any others.

**Project content rules**
- If the user has not specified project details, assume they want a "Hello World" project as a starting point.
- Avoid adding links or integrations that are not explicitly required.
- Do not generate images, videos, or other media unless explicitly requested.
- If placeholders are required, note that they should be replaced with real assets later.
- Ensure all generated components serve a clear purpose within the user's requested workflow.
- If a feature is assumed but not confirmed, prompt the user for clarification before including it.
- For VS Code extension work, use the VS Code API tool to find relevant references and samples.

**Task completion rules**
- The task is complete when:
	- The project is scaffolded and compiles without errors.
	- `.github/copilot-instructions.md` exists and is up to date.
	- `README.md` exists and reflects current project information.
	- The user has clear instructions for debugging or launching the project.

Before starting a new task in the above plan, update progress in the plan.
- Work through each checklist item systematically.
- Keep communication concise and focused.
- Follow development best practices.
