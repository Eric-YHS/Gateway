Codex Desktop: administrator elevation without UAC prompts

Effect
- ConsentPromptBehaviorAdmin is set from 5 to 0.
- EnableLUA remains enabled at 1.
- Administrator elevation requests are approved silently for every application
  running under an administrator account, not only Codex.

Restore
- Run Restore-DefaultAdminUacPrompt.ps1.
- It restores ConsentPromptBehaviorAdmin to the original value 5 and leaves
  EnableLUA enabled.

Files
- Enable-AdminElevationWithoutPrompt.ps1 applies the no-prompt policy and removes
  the earlier ineffective Codex scheduled-task launcher.
- Restore-DefaultAdminUacPrompt.ps1 restores the original prompt policy.
