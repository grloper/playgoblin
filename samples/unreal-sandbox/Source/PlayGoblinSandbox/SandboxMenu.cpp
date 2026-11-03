#include "SandboxMenu.h"

#include "Blueprint/WidgetTree.h"
#include "Components/Button.h"
#include "Components/TextBlock.h"
#include "Components/VerticalBox.h"
#include "Misc/CommandLine.h"

TSharedRef<SWidget> USandboxMenu::RebuildWidget()
{
	if (!WidgetTree) WidgetTree = NewObject<UWidgetTree>(this, TEXT("WidgetTree"), RF_Transient);
	if (!WidgetTree->RootWidget)
	{
		const bool bClean = FParse::Param(FCommandLine::Get(), TEXT("PGClean"));
		UVerticalBox* Root = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("Root"));
		WidgetTree->RootWidget = Root;

		UTextBlock* Title = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("Title"));
		Title->SetText(FText::FromString(bClean ? TEXT("Sandbox") : TEXT("TODO: title")));
		Root->AddChildToVerticalBox(Title);

		auto AddButton = [&](const TCHAR* Name, const TCHAR* Label) -> UButton*
		{
			UButton* B = WidgetTree->ConstructWidget<UButton>(UButton::StaticClass(), Name);
			UTextBlock* T = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), *FString::Printf(TEXT("%sLabel"), Name));
			T->SetText(FText::FromString(Label));
			B->SetContent(T);
			Root->AddChildToVerticalBox(B);
			return B;
		};
		// Options comes first: the crawler clicks top to bottom and "Play" closes the menu.
		if (!bClean) AddButton(TEXT("OptionsButton"), TEXT("Options")); // planted dead button: no handler
		AddButton(TEXT("PlayButton"), TEXT("Play"))->OnClicked.AddDynamic(this, &USandboxMenu::OnPlay);
	}
	return Super::RebuildWidget();
}

void USandboxMenu::OnPlay()
{
	RemoveFromParent();
}
