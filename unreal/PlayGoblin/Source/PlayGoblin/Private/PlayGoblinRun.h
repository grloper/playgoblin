#pragma once

#include "CoreMinimal.h"
#include "HAL/CriticalSection.h"
#include "Misc/OutputDevice.h"
#include "Math/RandomStream.h"
#include "PlayGoblinEvents.h"

class APawn;
class UWorld;

struct FPlayGoblinOptions
{
	int32 Seed = 42;
	float Minutes = 10.f;
	/** Seconds without progress before the player counts as stuck. */
	float StuckSeconds = 5.f;
	/** Horizontal movement (cm) below which, over StuckSeconds, the player counts as stuck. */
	float StuckDistanceCm = 50.f;
	float PositionHz = 2.f;
	float WaitForPlayerSeconds = 10.f;
	/** Only walk to ground at most this far (cm) above or below the level the player starts on. */
	float MaxClimbCm = 150.f;
	FString OutputRoot;
	bool bRecordFrames = true;
	float FrameHz = 2.f;
	float BurstHz = 10.f;
	float BurstSeconds = 3.f;
	float FpsBudgetMs = 0.f;
	float TimeScale = 1.f;
	bool bExitWhenDone = false;
	/** Drive the pawn with simulated key presses (W + control yaw) instead of AddMovementInput, so a missing or broken input binding shows up as a bot that cannot move. */
	bool bUseKeys = false;
	/** Crawl visible UMG menus (dead buttons, placeholder text). On by default; -PlayGoblinMenus=0 turns it off. */
	bool bMenus = true;
};

enum class EPGState : uint8 { Idle, Starting, Running, Ended };

struct FPGSnapshot
{
	EPGState State = EPGState::Idle;
	FString RunId;
	FString RunDir;
	FString EndReason;
	double T = 0.0;
	int64 Events = 0;
	int32 Stucks = 0;
	int32 Falls = 0;
	int32 Errors = 0;
	float Coverage = 0.f;
};

/** Groups identical error logs (by message, ignoring digits) like the Unity LogProbe does. */
class FPGLogCapture : public FOutputDevice
{
public:
	struct FGroup
	{
		FString Hash, Kind, Message;
		int32 Count = 0;
		double FirstT = 0, LastT = 0;
		bool bHasPos = false;
		FVector Pos = FVector::ZeroVector;
	};

	virtual void Serialize(const TCHAR* V, ELogVerbosity::Type Verbosity, const FName& Category) override;
	virtual bool CanBeUsedOnMultipleThreads() const override { return true; }

	void SetContext(double InT, bool bInHasPos, const FVector& InPos);
	TArray<FGroup> Groups() const;
	int32 GroupCount() const;

private:
	mutable FCriticalSection Gate;
	TMap<FString, FGroup> Map;
	TArray<FString> Order;
	double CurrentT = 0;
	bool bHasPos = false;
	FVector LastPos = FVector::ZeroVector;
};

/** One run: finds the player pawn, walks, detects problems and writes events.jsonl plus run.json. */
class FPlayGoblinRun
{
public:
	explicit FPlayGoblinRun(const FPlayGoblinOptions& InOptions);
	~FPlayGoblinRun();

	void Begin();
	void Tick(UWorld* World);
	void Stop() { End(TEXT("stopped")); }

	EPGState GetState() const { return State; }
	FPGSnapshot Snapshot() const;
	FString EventsJson(int64 Since, int32 Max) const;
	FString RunId, RunDirectory, EndReason;
	bool bExitWhenDone() const { return Options.bExitWhenDone; }

private:
	void Add(const FString& Type, const FString& Detail, const FVector* Pos, TArray<TPair<FString, FString>> Data = {});
	void Flush();
	void End(const FString& Reason);
	void WriteSummary(const TArray<FPGLogCapture::FGroup>& Groups);

	void OnPlayerFound(UWorld* World, APawn* Pawn);
	void BuildGrid(UWorld* World, APawn* Pawn);
	/** Reports visible actors that still look like a prototype (engine default/grid material, /Engine/BasicShapes mesh) as prototype_look events. */
	void ScanPrototypeLook(UWorld* World, APawn* Pawn);
	void PickDestination(UWorld* World, APawn* Pawn);
	void Walk(UWorld* World, APawn* Pawn, double T);
	/** Reads the game's real bindings (legacy key/axis bindings on the pawn and controller input components). */
	void DiscoverInputs(APawn* Pawn);
	/** Turns toward a yaw with simulated mouse movement, like a person would. Falls back to SetControlRotation if the mouse does nothing. */
	void HumanLook(APlayerController* PC, float DesiredYaw);
	void PressKey(APlayerController* PC, const FKey& Key, double T);
	void UpdateKeyPresses(APlayerController* PC, double T);
	bool FeedStuck(double T, const FVector& P, bool bTrying);
	void CaptureFrame(double T);
	int32 CellIndex(const FVector& P) const;

	FPlayGoblinOptions Options;
	TUniquePtr<class FPGMenuCrawler> Menus;
	EPGState State = EPGState::Idle;
	FRandomStream Rng;
	FString EventsPath;
	FArchive* Writer = nullptr;
	TUniquePtr<FPGLogCapture> Logs;

	mutable FCriticalSection EventGate;
	TArray<FPGEvent> Memory;
	TArray<FPGEvent> Pending;
	int64 SeqCounter = 0;
	TMap<FString, int32> Counts;

	bool bTimeInit = false;
	double StartWorldTime = 0, CurrentT = 0, EndT = 0, NextPos = 0, NextGood = 0;
	double StartReal = 0, WaitStartReal = 0, LastRealNow = 0;
	FDateTime StartedUtc;
	FString MapName, MoverKind;
	TWeakObjectPtr<APawn> PawnPtr;

	// Walking
	bool bUsesNav = false;
	TArray<FVector> Path;
	int32 PathIndex = 0;
	FVector Dest = FVector::ZeroVector;
	bool bKeyDown = false;
	TArray<FKey> ForwardKeys;
	TArray<FKey> ActionKeys;
	FKey JumpKey;
	bool bMouseLookBound = false;
	bool bMouseLookWorks = true;
	bool bMouseLookChecked = false;
	float MouseProbeSent = 0.f;
	float MouseProbeYaw = 0.f;
	bool bMouseProbeStarted = false;
	double MouseProbeReadyAt = -1.0; // first look attempt; probing starts 2 s later
	double MouseProbeBeganAt = 0.0;
	int32 MouseProbeFails = 0;
	FKey PressedKey;
	double ReleaseAt = -1.0;
	double NextActionAt = 3.0;
	int32 KeyPresses = 0;
	bool bHasDest = false;
	double DestSince = 0;
	int32 Waypoints = 0, Stucks = 0, Falls = 0, Respawns = 0;
	double DistanceCm = 0;
	FVector LastPos = FVector::ZeroVector, LastGood = FVector::ZeroVector;
	bool bHasLast = false;
	float FallZ = 0.f, HalfHeight = 88.f, Radius = 34.f;

	// Stuck window
	TArray<TPair<double, FVector>> StuckSamples;

	// Coverage grid
	TArray<FVector> Walkable;
	TArray<int32> WalkableCell;
	TSet<int32> Visited;
	/** Grid cell index -> index into Walkable. */
	TMap<int32, int32> CellToWalk;
	/** Destinations the bot gave up on (stuck / timeout); frontier picking skips them so it does not retry a blocked spot forever. */
	TSet<int32> Abandoned;
	/** Cells within this distance (cm) of the player count as visited (about what is on screen around a person). */
	static constexpr float VisitRadiusCm = 400.f;
	void MarkVisited(const FVector& P);
	/** Picks the nearest unvisited cell next to explored ground, weighted by distance. Returns the Walkable index or -1. */
	int32 PickFrontierCell(const FVector& From);
	float CoverageFraction() const;
	/** Stops the frontier picker from choosing this cell and its unvisited neighbours again (the spot was blocked). */
	void AbandonAround(const FVector& P);
	FVector GridMin = FVector::ZeroVector;
	int32 GridNx = 0, GridNz = 0;
	static constexpr float CellCm = 300.f;

	// Frames
	FString FramesDir;
	bool bRecording = false;
	FString RecorderOff;
	int32 FrameIndex = 0;
	double NextFrameReal = 0, BurstUntilReal = 0;
	uint64 LastCaptureFrameNo = 0;

	// Frame-rate watch
	double SlowSeconds = 0, SlowMsSum = 0, CooldownUntil = 0;
	int32 SlowFrames = 0;
	double IgnoreFpsUntil = 5.0;
	int32 FpsEvents = 0;
	float MaxFrameMs = 0, FrameMsSum = 0;
	int64 FrameCount = 0;
};
