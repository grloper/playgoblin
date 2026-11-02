#include "SandboxCharacter.h"

#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"

ASandboxCharacter::ASandboxCharacter()
{
	GetCapsuleComponent()->InitCapsuleSize(34.f, 88.f);
	GetCharacterMovement()->MaxWalkSpeed = 500.f;
	GetCharacterMovement()->MaxStepHeight = 45.f;

	USpringArmComponent* Boom = CreateDefaultSubobject<USpringArmComponent>(TEXT("Boom"));
	Boom->SetupAttachment(RootComponent);
	Boom->SetUsingAbsoluteRotation(true);
	Boom->TargetArmLength = 4500.f;
	Boom->SetRelativeRotation(FRotator(-80.f, 0.f, 0.f));
	Boom->bDoCollisionTest = false;

	UCameraComponent* Cam = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Cam->SetupAttachment(Boom);
}
