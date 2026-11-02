using UnrealBuildTool;

public class PlayGoblin : ModuleRules
{
	public PlayGoblin(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore" });
		PrivateDependencyModuleNames.AddRange(new string[] { "NavigationSystem", "HTTPServer", "Json", "Slate", "SlateCore" });
	}
}
