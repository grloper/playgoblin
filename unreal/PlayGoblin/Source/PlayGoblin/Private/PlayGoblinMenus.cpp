#include "PlayGoblinMenus.h"

#include "Blueprint/UserWidget.h"
#include "Blueprint/WidgetTree.h"
#include "Components/Button.h"
#include "Components/PanelWidget.h"
#include "Components/TextBlock.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "HAL/PlatformTime.h"
#include "Internationalization/Regex.h"
#include "UObject/UObjectIterator.h"

namespace
{
	bool ChainVisible(const UWidget* W)
	{
		for (const UWidget* C = W; C; C = C->GetParent())
			if (!C->IsVisible()) return false;
		return true;
	}

	/** Text of the first UTextBlock under a widget (depth first). */
	bool FirstText(UWidget* W, FString& Out)
	{
		if (UTextBlock* T = Cast<UTextBlock>(W)) { Out = T->GetText().ToString(); return true; }
		if (UPanelWidget* P = Cast<UPanelWidget>(W))
			for (int32 i = 0; i < P->GetChildrenCount(); ++i)
				if (FirstText(P->GetChildAt(i), Out)) return true;
		return false;
	}

	void Collect(UUserWidget* Root, TArray<UWidget*>& Out, int32 Depth = 0)
	{
		if (!Root || !Root->WidgetTree || Depth > 8) return;
		TArray<UWidget*> All;
		Root->WidgetTree->GetAllWidgets(All);
		for (UWidget* W : All)
		{
			Out.Add(W);
			if (UUserWidget* Nested = Cast<UUserWidget>(W)) Collect(Nested, Out, Depth + 1);
		}
	}

	/** Visible widgets in the viewport owned by this world, sorted by name so the crawl order is deterministic. */
	TArray<UUserWidget*> ViewportRoots(UWorld* World)
	{
		TArray<UUserWidget*> Roots;
		for (TObjectIterator<UUserWidget> It; It; ++It)
		{
			UUserWidget* W = *It;
			if (!W || W->IsTemplate() || !IsValid(W) || W->GetWorld() != World || !W->IsInViewport() || !W->IsVisible()) continue;
			Roots.Add(W);
		}
		Roots.Sort([](const UUserWidget& A, const UUserWidget& B) { return A.GetName() < B.GetName(); });
		return Roots;
	}

	FString OwnerClass(const UWidget* W)
	{
		const UObject* Outer = W->GetOuter();
		while (Outer && !Outer->IsA<UUserWidget>()) Outer = Outer->GetOuter();
		return Outer ? Outer->GetClass()->GetName() : TEXT("Widget");
	}

	bool IsPlaceholder(const FString& Text)
	{
		static const FRegexPattern Words(TEXT("(?i)\\b(todo|tbd|lorem|placeholder|fixme)\\b"));
		static const FRegexPattern Defaults(TEXT("(?i)^\\s*(text block|button)\\s*$"));
		FRegexMatcher A(Words, Text);
		if (A.FindNext()) return true;
		FRegexMatcher B(Defaults, Text);
		return B.FindNext();
	}

	uint64 Mix(uint64 H, uint64 V) { return (H ^ V) * 1099511628211ull; }
}

uint64 FPGMenuCrawler::Signature(UWorld* World) const
{
	uint64 H = 1469598103934665603ull;
	H = Mix(H, reinterpret_cast<uint64>(World));
	H = Mix(H, GetTypeHash(World->GetMapName()));
	H = Mix(H, World->IsPaused() ? 1 : 0);
	if (APlayerController* PC = World->GetFirstPlayerController()) H = Mix(H, PC->bShowMouseCursor ? 1 : 0);
	if (UGameViewportClient* VC = World->GetGameViewport()) H = Mix(H, (VC->IgnoreInput() ? 2 : 0) | (static_cast<uint64>(VC->GetMouseCaptureMode()) << 4));
	for (UUserWidget* Root : ViewportRoots(World))
	{
		H = Mix(H, reinterpret_cast<uint64>(Root));
		TArray<UWidget*> All;
		Collect(Root, All);
		for (UWidget* W : All)
			H = Mix(Mix(H, reinterpret_cast<uint64>(W)), (W->IsVisible() ? 1 : 0) | (W->GetIsEnabled() ? 2 : 0) | (static_cast<uint64>(W->GetVisibility()) << 4));
	}
	return H;
}

void FPGMenuCrawler::Scan(UWorld* World)
{
	for (UUserWidget* Root : ViewportRoots(World))
	{
		TArray<UWidget*> All;
		Collect(Root, All);
		for (UWidget* W : All)
		{
			if (!ChainVisible(W)) continue;
			const FString Path = OwnerClass(W) + TEXT("/") + W->GetName();
			if (UTextBlock* T = Cast<UTextBlock>(W))
			{
				const FString Text = T->GetText().ToString();
				if (IsPlaceholder(Text) && !PlaceholderSeen.Contains(Path + TEXT("|") + Text))
				{
					PlaceholderSeen.Add(Path + TEXT("|") + Text);
					Emit(TEXT("placeholder_text"), Text, { { TEXT("path"), Path } });
				}
			}
			else if (UButton* B = Cast<UButton>(W))
			{
				if (Clicked.Contains(Path) || Skipped.Contains(Path) || !B->GetIsEnabled()) continue;
				if (B->GetVisibility() == ESlateVisibility::HitTestInvisible || B->GetVisibility() == ESlateVisibility::SelfHitTestInvisible) continue;
				FString Label;
				FirstText(B, Label);
				const FString Lower = Label.ToLower();
				if (Lower.Contains(TEXT("quit")) || Lower.Contains(TEXT("exit")))
				{
					Skipped.Add(Path);
					Emit(TEXT("menu_skip"), Label, { { TEXT("path"), Path }, { TEXT("reason"), TEXT("quit_or_exit") } });
					continue;
				}
				bool bQueued = false;
				for (const FQueued& Q : Queue) bQueued |= Q.Path == Path;
				if (!bQueued) Queue.Add({ B, Path, Label });
			}
		}
	}
}

void FPGMenuCrawler::Tick(UWorld* World)
{
	if (!World) return;
	const double Now = FPlatformTime::Seconds();
	if (bWaiting)
	{
		if (Signature(World) != BaseSig) { bWaiting = false; NextScan = Now + 0.5; }
		else if (Now >= Deadline)
		{
			bWaiting = false;
			Emit(TEXT("dead_button"), WaitLabel, { { TEXT("path"), WaitPath }, { TEXT("label"), WaitLabel }, { TEXT("scene"), WaitScene } });
		}
		return;
	}
	if (Queue.Num() == 0)
	{
		if (Now < NextScan) return;
		NextScan = Now + 2.0;
		Scan(World);
	}
	while (Queue.Num() > 0)
	{
		if (ClickCount >= MaxClicks)
		{
			if (!bLimitReported) { bLimitReported = true; Emit(TEXT("menu_skip"), TEXT("click limit"), { { TEXT("reason"), TEXT("max_clicks") } }); }
			Queue.Reset();
			return;
		}
		FQueued Q = Queue[0];
		Queue.RemoveAt(0);
		UButton* B = Q.Button.Get();
		if (!B || !IsValid(B) || !ChainVisible(B) || !B->GetIsEnabled()) continue; // gone since the scan (e.g. an earlier click closed the menu)
		BaseSig = Signature(World);
		WaitPath = Q.Path; WaitLabel = Q.Label; WaitScene = World->GetMapName();
		Clicked.Add(Q.Path);
		ClickCount++;
		B->OnClicked.Broadcast();
		Deadline = Now + 1.0;
		bWaiting = true;
		return;
	}
}
