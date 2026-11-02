#include "PlayGoblinRun.h"

#include "Engine/World.h"
#include "Engine/StaticMesh.h"
#include "Components/StaticMeshComponent.h"
#include "Components/PrimitiveComponent.h"
#include "Materials/Material.h"
#include "Materials/MaterialInterface.h"
#include "EngineUtils.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/WorldSettings.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformTime.h"
#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "HAL/PlatformProperties.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "NavigationPath.h"
#include "NavigationSystem.h"
#include "UnrealClient.h"
#include "InputKeyEventArgs.h"
#include "InputCoreTypes.h"
#include "GameFramework/PlayerInput.h"
#include "Components/InputComponent.h"

DEFINE_LOG_CATEGORY(LogPlayGoblin);

// ---------------------------------------------------------------- log capture

void FPGLogCapture::Serialize(const TCHAR* V, ELogVerbosity::Type Verbosity, const FName& Category)
{
	if (Category == TEXT("LogPlayGoblin")) return;
	const bool bScript = Category == TEXT("LogScript") || Category == TEXT("LogBlueprintUserMessages");
	const bool bError = Verbosity == ELogVerbosity::Error || Verbosity == ELogVerbosity::Fatal;
	if (!bError && !(bScript && Verbosity == ELogVerbosity::Warning)) return;

	FString Line(V);
	int32 NewLine;
	if (Line.FindChar(TEXT('\n'), NewLine)) Line.LeftInline(NewLine);
	Line.TrimEndInline();

	FString Normalized;
	for (TCHAR C : Line) if (!FChar::IsDigit(C)) Normalized.AppendChar(C);
	const FString Kind = bScript ? TEXT("exception") : TEXT("error");
	const FString Hash = PlayGoblin::StableHash(Kind + TEXT("|") + Category.ToString() + TEXT("|") + Normalized);

	FScopeLock Lock(&Gate);
	if (FGroup* G = Map.Find(Hash))
	{
		G->Count++;
		G->LastT = CurrentT;
		return;
	}
	FGroup N;
	N.Hash = Hash; N.Kind = Kind; N.Message = Category.ToString() + TEXT(": ") + Line;
	N.Count = 1; N.FirstT = N.LastT = CurrentT; N.bHasPos = bHasPos; N.Pos = LastPos;
	Map.Add(Hash, N);
	Order.Add(Hash);
}

void FPGLogCapture::SetContext(double InT, bool bInHasPos, const FVector& InPos)
{
	FScopeLock Lock(&Gate);
	CurrentT = InT; bHasPos = bInHasPos; LastPos = InPos;
}

TArray<FPGLogCapture::FGroup> FPGLogCapture::Groups() const
{
	FScopeLock Lock(&Gate);
	TArray<FGroup> Out;
	for (const FString& H : Order) Out.Add(Map[H]);
	return Out;
}

int32 FPGLogCapture::GroupCount() const
{
	FScopeLock Lock(&Gate);
	return Map.Num();
}

// ---------------------------------------------------------------- run

FPlayGoblinRun::FPlayGoblinRun(const FPlayGoblinOptions& InOptions) : Options(InOptions), Rng(InOptions.Seed) {}

FPlayGoblinRun::~FPlayGoblinRun()
{
	if (State != EPGState::Ended && State != EPGState::Idle) End(TEXT("aborted"));
	if (Writer) { Writer->Close(); delete Writer; Writer = nullptr; }
}

void FPlayGoblinRun::Begin()
{
	StartedUtc = FDateTime::UtcNow();
	StartReal = WaitStartReal = FPlatformTime::Seconds();
	RunId = StartedUtc.ToString(TEXT("%Y%m%d-%H%M%S")) + FString::Printf(TEXT("-s%d"), Options.Seed);
	const FString Root = Options.OutputRoot.IsEmpty() ? FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("PlayGoblinRuns")) : Options.OutputRoot;
	RunDirectory = FPaths::ConvertRelativePathToFull(FPaths::Combine(Root, RunId));
	IFileManager::Get().MakeDirectory(*RunDirectory, true);
	EventsPath = FPaths::Combine(RunDirectory, TEXT("events.jsonl"));
	Writer = IFileManager::Get().CreateFileWriter(*EventsPath, FILEWRITE_AllowRead);

	Logs = MakeUnique<FPGLogCapture>();
	GLog->AddOutputDevice(Logs.Get());

	if (Options.bRecordFrames)
	{
		if (!FApp::CanEverRender()) RecorderOff = TEXT("no rendering device");
		else { bRecording = true; FramesDir = FPaths::Combine(RunDirectory, TEXT("frames")); }
	}
	else RecorderOff = TEXT("disabled in options");

	State = EPGState::Starting;
	Add(TEXT("run_start"), TEXT(""), nullptr, {
		{ TEXT("run_id"), RunId }, { TEXT("seed"), FString::FromInt(Options.Seed) },
		{ TEXT("minutes"), FString::SanitizeFloat(Options.Minutes) },
		{ TEXT("engine"), TEXT("unreal") }, { TEXT("unity"), TEXT("Unreal ") + FEngineVersion::Current().ToString(EVersionComponent::Patch) },
		{ TEXT("playgoblin"), TEXT("0.1.0-dev") } });
	Flush();
}

void FPlayGoblinRun::Add(const FString& Type, const FString& Detail, const FVector* Pos, TArray<TPair<FString, FString>> Data)
{
	FScopeLock Lock(&EventGate);
	FPGEvent E;
	E.Seq = ++SeqCounter; E.T = CurrentT; E.Type = Type; E.Detail = Detail;
	if (Pos) { E.bHasPos = true; E.Pos = *Pos; }
	E.Data = MoveTemp(Data);
	if (Memory.Num() >= 20000) Memory.RemoveAt(0);
	Memory.Add(E);
	Pending.Add(E);
}

void FPlayGoblinRun::Flush()
{
	if (!Writer) return;
	TArray<FPGEvent> Local;
	{
		FScopeLock Lock(&EventGate);
		Local = MoveTemp(Pending);
		Pending.Reset();
	}
	for (const FPGEvent& E : Local)
	{
		const FString Line = E.ToJson() + TEXT("\n");
		FTCHARToUTF8 Utf8(*Line);
		Writer->Serialize(const_cast<char*>(Utf8.Get()), Utf8.Length());
		Counts.FindOrAdd(E.Type)++;
		if (bRecording && (E.Type == TEXT("stuck") || E.Type == TEXT("out_of_world")))
			BurstUntilReal = FPlatformTime::Seconds() + Options.BurstSeconds;
	}
	Writer->Flush();
}

FPGSnapshot FPlayGoblinRun::Snapshot() const
{
	FPGSnapshot S;
	S.State = State; S.RunId = RunId; S.RunDir = RunDirectory; S.EndReason = EndReason;
	S.T = State == EPGState::Ended ? EndT : CurrentT;
	{
		FScopeLock Lock(&EventGate);
		S.Events = SeqCounter;
	}
	S.Stucks = Stucks; S.Falls = Falls;
	S.Errors = Logs ? Logs->GroupCount() : 0;
	S.Coverage = WalkableCell.Num() > 0 ? static_cast<float>(Visited.Num()) / WalkableCell.Num() : 0.f;
	return S;
}

FString FPlayGoblinRun::EventsJson(int64 Since, int32 Max) const
{
	FScopeLock Lock(&EventGate);
	FString Out = TEXT("{\"events\":[");
	int64 Next = Since;
	int32 N = 0;
	for (const FPGEvent& E : Memory)
	{
		if (E.Seq <= Since) continue;
		if (N >= Max) break;
		if (N++) Out += TEXT(",");
		Out += E.ToJson();
		Next = E.Seq;
	}
	return Out + FString::Printf(TEXT("],\"next\":%lld}"), Next);
}

void FPlayGoblinRun::Tick(UWorld* World)
{
	if (State == EPGState::Ended || State == EPGState::Idle || !World) return;

	const double Now = World->GetTimeSeconds();
	if (!bTimeInit)
	{
		bTimeInit = true;
		StartWorldTime = Now;
		if (!FMath::IsNearlyEqual(Options.TimeScale, 1.f))
			if (AWorldSettings* WS = World->GetWorldSettings()) WS->SetTimeDilation(Options.TimeScale);
		MapName = World->GetMapName();
	}
	CurrentT = Now - StartWorldTime;
	const double Real = FPlatformTime::Seconds();
	const float FrameMs = static_cast<float>(FApp::GetDeltaTime() * 1000.0);
	MaxFrameMs = FMath::Max(MaxFrameMs, FrameMs); FrameMsSum += FrameMs; FrameCount++;

	APawn* Pawn = PawnPtr.Get();
	if (State == EPGState::Starting)
	{
		APlayerController* PC = World->GetFirstPlayerController();
		Pawn = PC ? PC->GetPawn() : nullptr;
		if (Pawn) OnPlayerFound(World, Pawn);
		else if (Real - WaitStartReal > Options.WaitForPlayerSeconds)
		{
			Add(TEXT("setup_error"), TEXT("No player pawn found. PlayGoblin needs a possessed Pawn (ideally an ACharacter) in the level."), nullptr);
			Flush();
			End(TEXT("no_player"));
			return;
		}
	}
	else if (!Pawn)
	{
		State = EPGState::Starting;
		WaitStartReal = Real;
		bHasDest = false;
		Add(TEXT("player_lost"), TEXT(""), nullptr);
	}

	// Frame-rate watch: skip the first seconds and the frame a screenshot was requested in.
	if (Options.FpsBudgetMs > 0.f && CurrentT >= IgnoreFpsUntil && GFrameCounter - LastCaptureFrameNo > 1)
	{
		if (FrameMs <= Options.FpsBudgetMs) { SlowSeconds = SlowMsSum = 0; SlowFrames = 0; }
		else
		{
			SlowSeconds += FrameMs / 1000.0; SlowMsSum += FrameMs; SlowFrames++;
			if (SlowSeconds >= 1.0 && CurrentT >= CooldownUntil)
			{
				const FVector P = Pawn ? Pawn->GetActorLocation() : FVector::ZeroVector;
				Add(TEXT("fps_drop"), TEXT(""), Pawn ? &P : nullptr, {
					{ TEXT("ms"), FString::Printf(TEXT("%.1f"), SlowMsSum / SlowFrames) },
					{ TEXT("seconds"), FString::Printf(TEXT("%.1f"), SlowSeconds) },
					{ TEXT("budget_ms"), FString::Printf(TEXT("%.1f"), Options.FpsBudgetMs) } });
				CooldownUntil = CurrentT + 5.0; SlowSeconds = SlowMsSum = 0; SlowFrames = 0; FpsEvents++;
			}
		}
	}
	LastRealNow = Real;

	if (State == EPGState::Running && Pawn)
	{
		Walk(World, Pawn, CurrentT);
		if (bRecording) CaptureFrame(CurrentT);
		Logs->SetContext(CurrentT, true, Pawn->GetActorLocation());
	}
	else if (Logs) Logs->SetContext(CurrentT, false, FVector::ZeroVector);

	Flush();
	if (CurrentT >= Options.Minutes * 60.0) End(TEXT("completed"));
}

void FPlayGoblinRun::OnPlayerFound(UWorld* World, APawn* Pawn)
{
	PawnPtr = Pawn;
	if (const ACharacter* Ch = Cast<ACharacter>(Pawn))
	{
		if (const UCapsuleComponent* Cap = Ch->GetCapsuleComponent()) { HalfHeight = Cap->GetScaledCapsuleHalfHeight(); Radius = Cap->GetScaledCapsuleRadius(); }
		MoverKind = TEXT("CharacterMovement");
	}
	else MoverKind = TEXT("Pawn");

	UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(World);
	bUsesNav = Nav && Nav->GetDefaultNavDataInstance(FNavigationSystem::DontCreate) != nullptr;
	LastGood = Pawn->GetActorLocation();
	LastPos = LastGood; bHasLast = true;
	BuildGrid(World, Pawn);
	State = EPGState::Running;
	const FVector P = Pawn->GetActorLocation();
	Add(TEXT("player_found"), Pawn->GetName(), &P, {
		{ TEXT("mover"), MoverKind }, { TEXT("navmesh"), bUsesNav ? TEXT("true") : TEXT("false") },
		{ TEXT("reachable_cells"), FString::FromInt(Walkable.Num()) } });
	ScanPrototypeLook(World, Pawn);
	DiscoverInputs(Pawn);
}

void FPlayGoblinRun::ScanPrototypeLook(UWorld* World, APawn* Pawn)
{
	constexpr int32 MaxReported = 50;
	const UMaterial* DefaultMat = UMaterial::GetDefaultMaterial(MD_Surface);
	auto IsPlaceholderMaterial = [DefaultMat](const UMaterialInterface* M)
	{
		if (!M) return false;
		if (M == DefaultMat) return true;
		if (!M->GetPathName().StartsWith(TEXT("/Engine/"))) return false;
		const FString Name = M->GetName();
		return Name.Contains(TEXT("WorldGridMaterial")) || Name.Contains(TEXT("DefaultMaterial"))
			|| Name.Contains(TEXT("BasicShapeMaterial")) || Name.Contains(TEXT("Checker"));
	};

	struct FHit { FString Actor, Reason, Mesh, Material, Component; FVector Loc; };
	TArray<FHit> Hits;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		AActor* A = *It;
		if (A == Pawn || Cast<APawn>(A) || A->IsHidden() || A->IsEditorOnly()) continue;
		TInlineComponentArray<UPrimitiveComponent*> Prims;
		A->GetComponents(Prims);
		for (UPrimitiveComponent* C : Prims)
		{
			if (!C || !C->IsVisible() || C->IsEditorOnly()) continue;
			const UStaticMeshComponent* SMC = Cast<UStaticMeshComponent>(C);
			const UStaticMesh* Mesh = SMC ? SMC->GetStaticMesh() : nullptr;
			const FString MeshPath = Mesh ? Mesh->GetPathName() : FString();
			const bool bBasic = MeshPath.StartsWith(TEXT("/Engine/BasicShapes/"));
			FString BadMat;
			for (int32 i = 0, N = C->GetNumMaterials(); i < N; ++i)
			{
				const UMaterialInterface* M = C->GetMaterial(i);
				if (IsPlaceholderMaterial(M)) { BadMat = M->GetPathName(); break; }
			}
			if (!bBasic && BadMat.IsEmpty()) continue;
			FHit H;
#if WITH_EDITOR
			H.Actor = A->GetActorLabel();
#endif
			if (H.Actor.IsEmpty()) H.Actor = A->GetName();
			H.Reason = bBasic ? TEXT("basic_shape") : TEXT("default_material");
			H.Mesh = MeshPath; H.Material = BadMat; H.Component = C->GetName(); H.Loc = A->GetActorLocation();
			Hits.Add(MoveTemp(H));
			break; // one event per actor
		}
	}
	for (int32 i = 0; i < Hits.Num() && i < MaxReported; ++i)
	{
		const FHit& H = Hits[i];
		Add(TEXT("prototype_look"), H.Actor, &H.Loc, { { TEXT("reason"), H.Reason }, { TEXT("mesh"), H.Mesh }, { TEXT("material"), H.Material },
			{ TEXT("component"), H.Component }, { TEXT("total"), FString::FromInt(Hits.Num()) } });
	}
	UE_LOG(LogPlayGoblin, Log, TEXT("Prototype look: %d actor(s) with placeholder meshes/materials (%d reported)"), Hits.Num(), FMath::Min(Hits.Num(), MaxReported));
}

int32 FPlayGoblinRun::CellIndex(const FVector& P) const
{
	const int32 X = FMath::FloorToInt((P.X - GridMin.X) / CellCm);
	const int32 Y = FMath::FloorToInt((P.Y - GridMin.Y) / CellCm);
	return (X < 0 || Y < 0 || X >= GridNx || Y >= GridNz) ? -1 : Y * GridNx + X;
}

void FPlayGoblinRun::BuildGrid(UWorld* World, APawn* Pawn)
{
	FBox Bounds(ForceInit);
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		AActor* A = *It;
		if (A == Pawn || Cast<APawn>(A) || Cast<AWorldSettings>(A) || A->IsHidden()) continue;
		const FBox B = A->GetComponentsBoundingBox(false);
		if (B.IsValid && B.GetSize().GetMax() < 500000.f) Bounds += B;
	}
	const float LevelZ = Pawn->GetActorLocation().Z - HalfHeight;
	FallZ = LevelZ - 5000.f;
	if (!Bounds.IsValid) return;

	GridMin = Bounds.Min;
	GridNx = FMath::Max(1, FMath::CeilToInt(Bounds.GetSize().X / CellCm));
	GridNz = FMath::Max(1, FMath::CeilToInt(Bounds.GetSize().Y / CellCm));
	FCollisionQueryParams Q(SCENE_QUERY_STAT(PlayGoblinGrid), false, Pawn);
	const FCollisionShape Capsule = FCollisionShape::MakeCapsule(Radius * 0.9f, HalfHeight);
	float LowestGround = LevelZ;
	int32 RejNoHit = 0, RejSteep = 0, RejOverlap = 0;
	TArray<FString> IgnoredVolumes;
	for (int32 Y = 0; Y < GridNz; ++Y)
		for (int32 X = 0; X < GridNx; ++X)
		{
			const FVector C(GridMin.X + (X + 0.5f) * CellCm, GridMin.Y + (Y + 0.5f) * CellCm, 0.f);
			FHitResult Hit;
			// Start just above head height at the player's level, not above the whole level: a trace from the top of the
			// bounds lands on roofs, sky domes and ceilings and rejects every cell (found on the first real Unreal run).
			const FVector From(C.X, C.Y, LevelZ + Options.MaxClimbCm + 2.f * HalfHeight), To(C.X, C.Y, Bounds.Min.Z - 5000.f);
			// Trace with the channel the pawn's own movement collides with. ECC_Visibility also hits invisible helper volumes
			// and meshes that only block the camera, which the player walks straight through.
			bool bHit = false;
			// A trace that starts inside a blocking volume reports a hit at its own start point. A huge volume around the
			// player (found in the first real game: one actor spanning the whole level) would hide the ground in every cell,
			// so ignore actors the trace starts inside and trace again.
			for (int32 Try = 0; Try < 4; ++Try)
			{
				bHit = World->LineTraceSingleByChannel(Hit, From, To, ECC_Pawn, Q);
				if (!bHit || !Hit.bStartPenetrating || !Hit.GetActor()) break;
				IgnoredVolumes.AddUnique(Hit.GetActor()->GetName());
				Q.AddIgnoredActor(Hit.GetActor());
				bHit = false;
			}
			if (!bHit) { ++RejNoHit; continue; }
			if (Hit.ImpactNormal.Z < 0.7f || FMath::Abs(Hit.ImpactPoint.Z - LevelZ) > Options.MaxClimbCm) { ++RejSteep; continue; }
			const FVector Stand = Hit.ImpactPoint + FVector(0, 0, HalfHeight + 5.f);
			if (World->OverlapBlockingTestByChannel(Stand, FQuat::Identity, ECC_Pawn, Capsule, Q)) { ++RejOverlap; continue; }
			Walkable.Add(Hit.ImpactPoint);
			WalkableCell.Add(Y * GridNx + X);
			LowestGround = FMath::Min(LowestGround, Hit.ImpactPoint.Z);
		}
	FallZ = LowestGround - 1000.f;
	for (const FString& N : IgnoredVolumes) UE_LOG(LogTemp, Display, TEXT("PlayGoblin grid: ignored actor that encloses the player's level: %s"), *N);
	UE_LOG(LogTemp, Display, TEXT("PlayGoblin grid: %dx%d cells, walkable=%d, rejected: no-hit=%d steep-or-far=%d blocked=%d, LevelZ=%.0f, bounds=%s"),
		GridNx, GridNz, Walkable.Num(), RejNoHit, RejSteep, RejOverlap, LevelZ, *Bounds.ToString());
}

void FPlayGoblinRun::PickDestination(UWorld* World, APawn* Pawn)
{
	Path.Reset(); PathIndex = 0; bHasDest = false;
	const FVector From = Pawn->GetActorLocation();
	FVector Best = FVector::ZeroVector;
	bool bFound = false;

	if (bUsesNav)
	{
		if (UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(World))
		{
			for (int32 Try = 0; Try < 8 && !bFound; ++Try)
			{
				FNavLocation L;
				if (!Nav->GetRandomReachablePointInRadius(From, 6000.f, L)) continue;
				if (FVector::Dist2D(L.Location, From) < 400.f) continue;
				if (UNavigationPath* P = UNavigationSystemV1::FindPathToLocationSynchronously(World, From, L.Location, Pawn))
					if (P->IsValid() && !P->IsPartial() && P->PathPoints.Num() > 1) { Path = P->PathPoints; Best = L.Location; bFound = true; }
			}
		}
	}
	if (!bFound && Walkable.Num() > 0)
	{
		// Prefer cells not visited yet.
		for (int32 Try = 0; Try < 8; ++Try)
		{
			const int32 I = Rng.RandRange(0, Walkable.Num() - 1);
			if (FVector::Dist2D(Walkable[I], From) < 400.f) continue;
			Best = Walkable[I]; bFound = true;
			if (!Visited.Contains(WalkableCell[I])) break;
		}
	}
	if (!bFound) return;
	Dest = Best; bHasDest = true; DestSince = CurrentT;
	if (Path.Num() == 0) { Path.Add(Dest); }
	PathIndex = bUsesNav && Path.Num() > 1 ? 1 : 0;
}

bool FPlayGoblinRun::FeedStuck(double T, const FVector& P, bool bTrying)
{
	if (!bTrying) { StuckSamples.Reset(); return false; }
	StuckSamples.Add({ T, P });
	while (StuckSamples.Num() > 0 && T - StuckSamples[0].Key > Options.StuckSeconds) StuckSamples.RemoveAt(0);
	if (StuckSamples.Num() < 2 || T - StuckSamples[0].Key < Options.StuckSeconds * 0.9) return false;
	FVector2D Min(MAX_flt, MAX_flt), Max(-MAX_flt, -MAX_flt);
	for (const auto& S : StuckSamples)
	{
		Min.X = FMath::Min(Min.X, S.Value.X); Min.Y = FMath::Min(Min.Y, S.Value.Y);
		Max.X = FMath::Max(Max.X, S.Value.X); Max.Y = FMath::Max(Max.Y, S.Value.Y);
	}
	if (FMath::Max(Max.X - Min.X, Max.Y - Min.Y) < Options.StuckDistanceCm) { StuckSamples.Reset(); return true; }
	return false;
}

void FPlayGoblinRun::Walk(UWorld* World, APawn* Pawn, double T)
{
	const FVector Loc = Pawn->GetActorLocation();
	if (bHasLast) DistanceCm += FVector::Dist2D(Loc, LastPos);
	LastPos = Loc; bHasLast = true;
	const int32 Cell = CellIndex(Loc);
	if (Cell >= 0) Visited.Add(Cell);

	const UCharacterMovementComponent* CMC = Pawn->FindComponentByClass<UCharacterMovementComponent>();
	const bool bGrounded = !CMC || CMC->IsMovingOnGround();

	if (T >= NextPos)
	{
		NextPos = T + 1.0 / FMath::Max(0.1f, Options.PositionHz);
		Add(TEXT("pos"), TEXT(""), &Loc);
	}
	if (bGrounded && T >= NextGood) { NextGood = T + 1.0; LastGood = Loc; }

	if (Loc.Z < FallZ)
	{
		Falls++;
		Add(TEXT("out_of_world"), TEXT("below_world"), &Loc, { { TEXT("reason"), TEXT("below_world") }, { TEXT("fell_to"), PlayGoblin::MetresCsv(Loc) } });
		const FVector To = LastGood + FVector(0, 0, HalfHeight + 20.f);
		Pawn->SetActorLocation(To, false, nullptr, ETeleportType::TeleportPhysics);
		if (UCharacterMovementComponent* M = Pawn->FindComponentByClass<UCharacterMovementComponent>()) { M->StopMovementImmediately(); M->SetMovementMode(MOVE_Falling); }
		Respawns++;
		Add(TEXT("respawn"), TEXT(""), &To, { { TEXT("from"), PlayGoblin::MetresCsv(Loc) } });
		StuckSamples.Reset(); bHasDest = false; bHasLast = false;
		return;
	}

	if (!bHasDest || T - DestSince > 40.0)
	{
		if (bHasDest)
		{
			APlayerController* PCDiag = Cast<APlayerController>(Pawn->GetController());
			Add(TEXT("waypoint_timeout"), TEXT(""), &Loc, {
				{ TEXT("mode"), CMC ? StaticEnum<EMovementMode>()->GetNameStringByValue(CMC->MovementMode) : TEXT("none") },
				{ TEXT("move_ignored"), PCDiag && PCDiag->IsMoveInputIgnored() ? TEXT("true") : TEXT("false") },
				{ TEXT("has_controller"), Pawn->GetController() ? TEXT("true") : TEXT("false") },
				{ TEXT("max_speed"), FString::SanitizeFloat(CMC ? CMC->MaxWalkSpeed : 0.f) },
				{ TEXT("velocity_cm_s"), FString::SanitizeFloat(Pawn->GetVelocity().Size()) },
				{ TEXT("floor_hit"), CMC && CMC->CurrentFloor.bBlockingHit ? TEXT("true") : TEXT("false") },
				{ TEXT("floor_actor"), CMC && CMC->CurrentFloor.HitResult.GetActor() ? CMC->CurrentFloor.HitResult.GetActor()->GetName() : TEXT("") },
				{ TEXT("gravity_scale"), FString::SanitizeFloat(CMC ? CMC->GravityScale : 0.f) },
				{ TEXT("custom_time_dilation"), FString::SanitizeFloat(Pawn->CustomTimeDilation) },
				{ TEXT("dest"), PlayGoblin::MetresCsv(Dest) } });
		}
		PickDestination(World, Pawn);
		if (!bHasDest) return;
	}

	const FVector Target = Path.IsValidIndex(PathIndex) ? Path[PathIndex] : Dest;
	FVector Dir = Target - Loc; Dir.Z = 0;
	const bool bFinal = PathIndex >= Path.Num() - 1;
	if (Dir.Size() < (bFinal ? 150.f : 120.f))
	{
		if (bKeyDown)
		{
			if (APlayerController* KeyPC = Cast<APlayerController>(Pawn->GetController()))
				KeyPC->InputKey(FInputKeyEventArgs::CreateSimulated(ForwardKeys.Num() > 0 ? ForwardKeys[0] : EKeys::W, IE_Released, 0.f, 1, FInputDeviceId::CreateFromInternalId(0)));
			bKeyDown = false;
		}
		if (bFinal)
		{
			Waypoints++;
			Add(TEXT("waypoint"), TEXT(""), &Dest);
			bHasDest = false;
		}
		else PathIndex++;
		return;
	}
	Dir.Normalize();
	if (Options.bUseKeys)
	{
		if (APlayerController* KeyPC = Cast<APlayerController>(Pawn->GetController()))
		{
			HumanLook(KeyPC, Dir.Rotation().Yaw);
			// Walk only roughly facing the target, like a person who turns, then goes.
			const float Err = FMath::Abs(FRotator::NormalizeAxis(Dir.Rotation().Yaw - KeyPC->GetControlRotation().Yaw));
			const FKey Fwd = ForwardKeys.Num() > 0 ? ForwardKeys[0] : EKeys::W;
			if (Err < 50.f)
			{
				KeyPC->InputKey(FInputKeyEventArgs::CreateSimulated(Fwd, bKeyDown ? IE_Repeat : IE_Pressed, 1.f, 1, FInputDeviceId::CreateFromInternalId(0)));
				bKeyDown = true;
			}
			else if (bKeyDown)
			{
				KeyPC->InputKey(FInputKeyEventArgs::CreateSimulated(Fwd, IE_Released, 0.f, 1, FInputDeviceId::CreateFromInternalId(0)));
				bKeyDown = false;
			}
			UpdateKeyPresses(KeyPC, T);
		}
	}
	else
	{
		Pawn->AddMovementInput(Dir, 1.f);
	}

	if (bGrounded && FeedStuck(T, Loc, true))
	{
		Stucks++;
		FString Blocked;
		FHitResult Hit;
		FCollisionQueryParams Q(SCENE_QUERY_STAT(PlayGoblinStuck), false, Pawn);
		if (World->LineTraceSingleByChannel(Hit, Loc, Loc + Dir * 200.f, ECC_Visibility, Q) && Hit.GetActor())
			Blocked = Hit.GetActor()->GetName();
		if (Options.bUseKeys && JumpKey.IsValid())
			if (APlayerController* HopPC = Cast<APlayerController>(Pawn->GetController())) PressKey(HopPC, JumpKey, T); // a person tries hopping over it
		Add(TEXT("stuck"), FString::Printf(TEXT("no progress for %.0fs"), Options.StuckSeconds), &Loc, {
			{ TEXT("blocked_by"), Blocked },
			{ TEXT("controller"), CMC ? StaticEnum<EMovementMode>()->GetNameStringByValue(CMC->MovementMode) : TEXT("") } });
		bHasDest = false;
	}
}

void FPlayGoblinRun::CaptureFrame(double T)
{
	const double Real = FPlatformTime::Seconds();
	if (Real < NextFrameReal) return;
	const float Hz = Real < BurstUntilReal ? Options.BurstHz : Options.FrameHz;
	NextFrameReal = Real + 1.0 / FMath::Max(0.1f, Hz);
	if (FrameIndex >= 3000) return;
	IFileManager::Get().MakeDirectory(*FramesDir, true);
	const FString Name = FString::Printf(TEXT("f%05d_t%.2f.png"), FrameIndex++, T);
	LastCaptureFrameNo = GFrameCounter;
	FScreenshotRequest::RequestScreenshot(FPaths::Combine(FramesDir, Name), false, false);
}

void FPlayGoblinRun::End(const FString& Reason)
{
	if (State == EPGState::Ended || State == EPGState::Idle) return;
	EndReason = Reason;
	EndT = CurrentT;
	TArray<FPGLogCapture::FGroup> Groups;
	if (Logs)
	{
		Groups = Logs->Groups();
		for (const FPGLogCapture::FGroup& G : Groups)
		{
			Add(TEXT("error_summary"), G.Message, G.bHasPos ? &G.Pos : nullptr, {
				{ TEXT("hash"), G.Hash }, { TEXT("kind"), G.Kind }, { TEXT("count"), FString::FromInt(G.Count) },
				{ TEXT("first_t"), FString::Printf(TEXT("%.3f"), G.FirstT) }, { TEXT("last_t"), FString::Printf(TEXT("%.3f"), G.LastT) } });
		}
	}
	Add(TEXT("run_end"), Reason, nullptr);
	Flush();
	if (Logs) { GLog->RemoveOutputDevice(Logs.Get()); }
	WriteSummary(Groups);
	if (Writer) { Writer->Close(); delete Writer; Writer = nullptr; }
	State = EPGState::Ended;
	UE_LOG(LogPlayGoblin, Display, TEXT("Run %s ended (%s). Folder: %s"), *RunId, *Reason, *RunDirectory);
}

void FPlayGoblinRun::WriteSummary(const TArray<FPGLogCapture::FGroup>& Groups)
{
	using namespace PlayGoblin;
	const float Cov = WalkableCell.Num() > 0 ? static_cast<float>(Visited.Num()) / WalkableCell.Num() : 0.f;
	FString J = TEXT("{");
	auto S = [&](const TCHAR* K, const FString& V) { J += FString::Printf(TEXT("\"%s\":\"%s\","), K, *JsonEscape(V)); };
	auto N = [&](const TCHAR* K, double V) { J += FString::Printf(TEXT("\"%s\":%s,"), K, *Num(V)); };
	S(TEXT("run_id"), RunId); S(TEXT("playgoblin"), TEXT("0.1.0-dev")); S(TEXT("engine"), TEXT("unreal"));
	S(TEXT("engine_version"), TEXT("Unreal ") + FEngineVersion::Current().ToString(EVersionComponent::Patch));
	S(TEXT("platform"), FPlatformProperties::IniPlatformName()); S(TEXT("scene"), MapName);
	N(TEXT("seed"), Options.Seed); N(TEXT("planned_minutes"), Options.Minutes);
	S(TEXT("started_utc"), StartedUtc.ToIso8601()); S(TEXT("end_reason"), EndReason);
	N(TEXT("game_seconds"), EndT); N(TEXT("real_seconds"), FPlatformTime::Seconds() - StartReal);
	N(TEXT("time_scale"), Options.TimeScale);
	S(TEXT("mover"), MoverKind);
	J += FString::Printf(TEXT("\"navmesh\":%s,\"grid\":%s,"), bUsesNav ? TEXT("true") : TEXT("false"), Walkable.Num() > 0 ? TEXT("true") : TEXT("false"));
	N(TEXT("walkable_cells"), WalkableCell.Num()); N(TEXT("reachable_cells"), Walkable.Num());
	N(TEXT("coverage"), Cov); N(TEXT("distance_m"), DistanceCm / 100.0);
	N(TEXT("stuck"), Stucks); N(TEXT("out_of_world"), Falls); N(TEXT("respawns"), Respawns); N(TEXT("waypoints"), Waypoints);
	N(TEXT("recorded_frames"), FrameIndex);
	if (!RecorderOff.IsEmpty()) S(TEXT("recorder_off"), RecorderOff);
	J += FString::Printf(TEXT("\"frames\":{\"frames\":%lld,\"avg_ms\":%s,\"max_ms\":%s},"), FrameCount,
		*Num(FrameCount ? FrameMsSum / FrameCount : 0.0), *Num(MaxFrameMs));
	J += TEXT("\"event_counts\":{");
	bool bFirst = true;
	for (const auto& KV : Counts) { J += FString::Printf(TEXT("%s\"%s\":%d"), bFirst ? TEXT("") : TEXT(","), *JsonEscape(KV.Key), KV.Value); bFirst = false; }
	J += TEXT("},\"errors\":[");
	for (int32 i = 0; i < Groups.Num(); ++i)
	{
		const FPGLogCapture::FGroup& G = Groups[i];
		J += FString::Printf(TEXT("%s{\"hash\":\"%s\",\"kind\":\"%s\",\"message\":\"%s\",\"count\":%d,\"first_t\":%s,\"last_t\":%s}"),
			i ? TEXT(",") : TEXT(""), *G.Hash, *G.Kind, *JsonEscape(G.Message), G.Count, *Num(G.FirstT), *Num(G.LastT));
	}
	J += TEXT("]}");
	FFileHelper::SaveStringToFile(J, *FPaths::Combine(RunDirectory, TEXT("run.json")), FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

void FPlayGoblinRun::DiscoverInputs(APawn* Pawn)
{
	APlayerController* PC = Cast<APlayerController>(Pawn->GetController());
	UPlayerInput* PlayerIn = PC ? PC->PlayerInput.Get() : nullptr;
	TArray<UInputComponent*> Comps;
	if (Pawn->InputComponent) Comps.Add(Pawn->InputComponent);
	if (PC && PC->InputComponent) Comps.Add(PC->InputComponent);

	TArray<FString> Axes;
	bool bEnhanced = false;
	for (UInputComponent* C : Comps)
	{
		if (C->GetClass()->GetName().Contains(TEXT("Enhanced"))) bEnhanced = true;
		for (const FInputKeyBinding& KB : C->KeyBindings)
		{
			const FKey K = KB.Chord.Key;
			if (KB.KeyEvent != IE_Pressed || !K.IsValid() || K == EKeys::Escape) continue;
			if (K == EKeys::SpaceBar || (!JumpKey.IsValid() && K == EKeys::Gamepad_FaceButton_Bottom)) JumpKey = K;
			if (!K.IsGamepadKey()) ActionKeys.AddUnique(K);
		}
		for (const FInputAxisBinding& AB : C->AxisBindings)
		{
			Axes.AddUnique(AB.AxisName.ToString());
			if (!PlayerIn) continue;
			const FString Name = AB.AxisName.ToString();
			const bool bForwardAxis = Name.Contains(TEXT("Forward")) || Name.Contains(TEXT("Vertical")) || Name.Contains(TEXT("MoveY"));
			for (const FInputAxisKeyMapping& M : PlayerIn->GetKeysForAxis(AB.AxisName))
			{
				if (M.Key == EKeys::MouseX) bMouseLookBound = true;
				if (bForwardAxis && M.Scale > 0.f && !M.Key.IsAnalog() && !M.Key.IsGamepadKey()) ForwardKeys.AddUnique(M.Key);
			}
		}
		for (const FInputAxisKeyBinding& AKB : C->AxisKeyBindings)
			if (AKB.AxisKey == EKeys::MouseX) bMouseLookBound = true;
	}
	ForwardKeys.Sort([](const FKey& A, const FKey& B) { return A == EKeys::W; }); // prefer W when several keys move forward

	auto Join = [](const TArray<FKey>& Ks) { TArray<FString> S; for (const FKey& K : Ks) S.Add(K.ToString()); return FString::Join(S, TEXT(",")); };
	const FVector P = Pawn->GetActorLocation();
	Add(TEXT("input_map"), TEXT(""), &P, {
		{ TEXT("forward_keys"), Join(ForwardKeys) }, { TEXT("action_keys"), Join(ActionKeys) },
		{ TEXT("jump_key"), JumpKey.IsValid() ? JumpKey.ToString() : TEXT("") },
		{ TEXT("mouse_look"), bMouseLookBound ? TEXT("true") : TEXT("false") },
		{ TEXT("axes"), FString::Join(Axes, TEXT(",")) }, { TEXT("enhanced_input"), bEnhanced ? TEXT("true") : TEXT("false") } });
	if (!bEnhanced)
	{
		if (ForwardKeys.Num() == 0)
			Add(TEXT("dead_input"), TEXT("No keyboard key moves the player forward"), &P, { { TEXT("input"), TEXT("move") }, { TEXT("axes"), FString::Join(Axes, TEXT(",")) } });
		if (!bMouseLookBound)
			Add(TEXT("dead_input"), TEXT("Moving the mouse does not turn the camera (no MouseX binding)"), &P, { { TEXT("input"), TEXT("look") } });
	}
}

void FPlayGoblinRun::HumanLook(APlayerController* PC, float DesiredYaw)
{
	const float Yaw = PC->GetControlRotation().Yaw;
	const float Err = FRotator::NormalizeAxis(DesiredYaw - Yaw);
	if (!bMouseLookBound || !bMouseLookWorks)
	{
		FRotator R = PC->GetControlRotation(); R.Yaw = Yaw + FMath::Clamp(Err, -12.f, 12.f); // turn smoothly, not in one snap
		PC->SetControlRotation(R);
		return;
	}
	if (FMath::Abs(Err) < 2.f) return;
	// Mouse counts: proportional, clamped like a wrist flick. The game's own sensitivity decides how far that turns.
	const float MouseCounts = FMath::Clamp(Err * 1.5f, -40.f, 40.f);
	if (!bMouseLookChecked)
	{
		if (!bMouseProbeStarted) { bMouseProbeStarted = true; MouseProbeYaw = Yaw; }
		MouseProbeSent += FMath::Abs(MouseCounts);
		if (MouseProbeSent > 400.f)
		{
			bMouseLookChecked = true;
			if (FMath::Abs(FRotator::NormalizeAxis(Yaw - MouseProbeYaw)) < 0.5f)
			{
				bMouseLookWorks = false;
				const FVector P = PC->GetPawn() ? PC->GetPawn()->GetActorLocation() : FVector::ZeroVector;
				Add(TEXT("dead_input"), TEXT("Moving the mouse does not turn the camera"), &P, { { TEXT("input"), TEXT("look") }, { TEXT("mouse_counts_sent"), FString::SanitizeFloat(MouseProbeSent) } });
				return;
			}
		}
	}
	PC->InputKey(FInputKeyEventArgs::CreateSimulated(EKeys::MouseX, IE_Axis, MouseCounts, 1, FInputDeviceId::CreateFromInternalId(0)));
}

void FPlayGoblinRun::PressKey(APlayerController* PC, const FKey& Key, double T)
{
	if (ReleaseAt >= 0.0 || !Key.IsValid()) return; // one tap at a time
	PC->InputKey(FInputKeyEventArgs::CreateSimulated(Key, IE_Pressed, 1.f, 1, FInputDeviceId::CreateFromInternalId(0)));
	PressedKey = Key; ReleaseAt = T + 0.15; KeyPresses++;
	const FVector P = PC->GetPawn() ? PC->GetPawn()->GetActorLocation() : FVector::ZeroVector;
	Add(TEXT("input_press"), Key.ToString(), &P);
}

void FPlayGoblinRun::UpdateKeyPresses(APlayerController* PC, double T)
{
	if (ReleaseAt >= 0.0 && T >= ReleaseAt)
	{
		PC->InputKey(FInputKeyEventArgs::CreateSimulated(PressedKey, IE_Released, 0.f, 1, FInputDeviceId::CreateFromInternalId(0)));
		ReleaseAt = -1.0;
	}
	if (T >= NextActionAt && ActionKeys.Num() > 0)
	{
		PressKey(PC, ActionKeys[Rng.RandRange(0, ActionKeys.Num() - 1)], T);
		NextActionAt = T + Rng.FRandRange(2.5f, 6.0f);
	}
}
