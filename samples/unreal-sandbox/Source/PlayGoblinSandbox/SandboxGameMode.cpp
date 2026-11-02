#include "SandboxGameMode.h"

#include "Components/DirectionalLightComponent.h"
#include "Components/SkyLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/SkyLight.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/World.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerStart.h"
#include "Kismet/GameplayStatics.h"
#include "Misc/CommandLine.h"
#include "SandboxCharacter.h"

DEFINE_LOG_CATEGORY_STATIC(LogSandbox, Log, All);

ASandboxGameMode::ASandboxGameMode()
{
	DefaultPawnClass = ASandboxCharacter::StaticClass();
	PrimaryActorTick.bCanEverTick = true;
}

AStaticMeshActor* ASandboxGameMode::Box(const FVector& Min, const FVector& Max)
{
	UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));
	AStaticMeshActor* A = GetWorld()->SpawnActor<AStaticMeshActor>(FVector((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2), FRotator::ZeroRotator);
	A->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
	A->GetStaticMeshComponent()->SetStaticMesh(Cube);
	A->SetActorScale3D((Max - Min) / 100.f);
	return A;
}

void ASandboxGameMode::InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage)
{
	Super::InitGame(MapName, Options, ErrorMessage);
	bClean = FParse::Param(FCommandLine::Get(), TEXT("PGClean"));

	// Floor 60 x 60 m with a hole where the trapdoor tile sits. Heights in cm, top of the floor at z = 0.
	const float H = 3000.f, Z0 = -100.f, Z1 = 0.f;
	const float HX0 = -1200.f, HX1 = -400.f, HY0 = -1200.f, HY1 = -400.f;
	Box(FVector(-H, -H, Z0), FVector(HX0, H, Z1));
	Box(FVector(HX1, -H, Z0), FVector(H, H, Z1));
	Box(FVector(HX0, -H, Z0), FVector(HX1, HY0, Z1));
	Box(FVector(HX0, HY1, Z0), FVector(HX1, H, Z1));
	Trapdoor = Box(FVector(HX0, HY0, Z0), FVector(HX1, HY1, Z1));
	TrapBounds = FBox(FVector(HX0, HY0, Z0), FVector(HX1, HY1, Z1));

	// A long step across the level: 70 cm (bug) or a 20 cm kerb (clean).
	const float Step = bClean ? 20.f : 70.f;
	Box(FVector(-500.f, -100.f, 0.f), FVector(3000.f, 100.f, Step));

	GetWorld()->SpawnActor<APlayerStart>(FVector(-2000.f, -2000.f, 120.f), FRotator::ZeroRotator);
	if (ADirectionalLight* Sun = GetWorld()->SpawnActor<ADirectionalLight>(FVector(0, 0, 1000), FRotator(-50, 30, 0)))
	{
		Sun->GetLightComponent()->SetMobility(EComponentMobility::Movable);
		Sun->GetLightComponent()->SetIntensity(8.f);
	}
	if (ASkyLight* Sky = GetWorld()->SpawnActor<ASkyLight>(FVector(0, 0, 1000), FRotator::ZeroRotator))
	{
		Sky->GetLightComponent()->SetMobility(EComponentMobility::Movable);
		Sky->GetLightComponent()->SetIntensity(1.f);
	}
	UE_LOG(LogSandbox, Display, TEXT("Sandbox level built (%s)."), bClean ? TEXT("clean") : TEXT("with planted bugs"));
}

void ASandboxGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	APawn* Pawn = UGameplayStatics::GetPlayerPawn(this, 0);
	if (!Pawn) return;
	const FVector P = Pawn->GetActorLocation();

	if (!bClean && !bTrapOpen && Trapdoor)
	{
		const bool bInside = P.X > TrapBounds.Min.X && P.X < TrapBounds.Max.X && P.Y > TrapBounds.Min.Y && P.Y < TrapBounds.Max.Y && P.Z < 300.f;
		StandSeconds = bInside ? StandSeconds + DeltaSeconds : 0.f;
		if (StandSeconds > 0.4f)
		{
			bTrapOpen = true;
			Trapdoor->SetActorEnableCollision(false);
			Trapdoor->SetActorHiddenInGame(true);
		}
	}
	if (P.Z < -200.f) bHasDied = true;
	// PLANTED BUG: after the first fall the death screen reads a widget that was never created.
	if (!bClean && bHasDied) UE_LOG(LogSandbox, Error, TEXT("DeathScreen: Widget is null, cannot show respawn message"));
}
