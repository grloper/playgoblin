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

	explicit FPGMenuCrawler(FEmit InEmit, int32 InMaxClicks = 30) : Emit(MoveTemp(InEmit)), MaxClicks(InMaxClicks) {}

	void Tick(UWorld* World);
	int32 Clicks() const { return ClickCount; }

private:
	void Scan(UWorld* World);
	uint64 Signature(UWorld* World) const;

	FEmit Emit;
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
