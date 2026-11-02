#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "SandboxCharacter.generated.h"

/** A plain ACharacter with a camera high above it, so captured frames show the whole level. */
UCLASS()
class ASandboxCharacter : public ACharacter
{
	GENERATED_BODY()

public:
	ASandboxCharacter();
};
