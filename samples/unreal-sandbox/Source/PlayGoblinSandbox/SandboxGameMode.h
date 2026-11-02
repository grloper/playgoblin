#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "SandboxGameMode.generated.h"

class AStaticMeshActor;

/**
 * Builds a small level at startup. Unless the game is started with -PGClean it contains three planted bugs:
 *  - a trapdoor tile that gives way under the player (fall out of the world),
 *  - a 70 cm step across the level (the plugin allows 150 cm of climb, the character climbs 45 cm: stuck spot),
 *  - an error logged every frame once the player has fallen once.
 */
UCLASS()
class ASandboxGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	ASandboxGameMode();
	virtual void InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage) override;
	virtual void Tick(float DeltaSeconds) override;

private:
	AStaticMeshActor* Box(const FVector& Min, const FVector& Max);

	UPROPERTY() TObjectPtr<AStaticMeshActor> Trapdoor;
	bool bClean = false;
	bool bTrapOpen = false;
	bool bHasDied = false;
	float StandSeconds = 0.f;
	FBox TrapBounds = FBox(ForceInit);
};
