using UnrealBuildTool;

public class PlayGoblinSandboxTarget : TargetRules
{
	public PlayGoblinSandboxTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("PlayGoblinSandbox");
	}
}
