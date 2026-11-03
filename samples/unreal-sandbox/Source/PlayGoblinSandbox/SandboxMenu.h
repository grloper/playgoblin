#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "SandboxMenu.generated.h"

/**
 * Tiny menu built in C++. Planted (default): title "TODO: title" (placeholder text) and an "Options" button that does nothing
 * (dead button), plus "Play" which closes the menu. With -PGClean: a normal title and only "Play".
 */
UCLASS()
class USandboxMenu : public UUserWidget
{
	GENERATED_BODY()

protected:
	virtual TSharedRef<SWidget> RebuildWidget() override;

private:
	UFUNCTION() void OnPlay();
};
