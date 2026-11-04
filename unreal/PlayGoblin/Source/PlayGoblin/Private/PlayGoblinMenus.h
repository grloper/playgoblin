#pragma once

#include "CoreMinimal.h"

class UWorld;
class UButton;

/**
 * Crawls visible UMG menus like the Unity MenuCrawler: clicks each enabled button once (max 30 per run, never Quit/Exit),
 * reports buttons that change nothing within 1 s as dead_button, and visible placeholder text as placeholder_text.
 * Clicks are simulated with UButton::OnClicked.Broadcast(): the game's click handler runs, but Slate hit testing,
 * hover/press styles and the input stack are not exercised.
 */
class FPGMenuCrawler
{
public:
	using FEmit = TFunction<void(const FString& Type, const FString& Detail, TArray<TPair<FString, FString>> Data)>;

	explicit FPGMenuCrawler(FEmit InEmit, int32 InMaxClicks = 30);
	~FPGMenuCrawler();

	void Tick(UWorld* World);
	int32 Clicks() const { return ClickCount; }

	/** Seconds a freshly loaded world is left alone before anything is scanned or clicked (a person reads the screen first;
	 *  games also run their own start-up flow, e.g. an auto-travel timer, in that time). */
	static constexpr double SettleSeconds = 1.0;

private:
	void Scan(UWorld* World);
	/** Drops everything that points into the previous world (queued buttons, wait state, seen paths). The click budget is kept. */
	void ResetWorldState();
	void OnWorldCleanup(UWorld* InWorld, bool bSessionEnded, bool bCleanupResources);
	void OnPostLoadMap(UWorld* InWorld);
	uint64 Signature(UWorld* World) const;

	FEmit Emit;
	/** The world the queue/wait state belongs to. Weak: the world may be destroyed while we hold it. */
	TWeakObjectPtr<UWorld> TrackedWorld;
	FDelegateHandle CleanupHandle, PostLoadHandle;
	int32 MaxClicks;
	int32 ClickCount = 0;
	double NextScan = 0;

	bool bWaiting = false;
	uint64 BaseSig = 0;
	double Deadline = 0;
	FString WaitPath, WaitLabel, WaitScene;

	struct FQueued { TWeakObjectPtr<UButton> Button; FString Path, Label; };
	TArray<FQueued> Queue;
	TSet<FString> Clicked, Skipped, PlaceholderSeen;
	bool bLimitReported = false;
};
