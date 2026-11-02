using UnrealBuildTool;

public class PlayGoblinSandboxEditorTarget : TargetRules
{
	public PlayGoblinSandboxEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("PlayGoblinSandbox");
	}
}
