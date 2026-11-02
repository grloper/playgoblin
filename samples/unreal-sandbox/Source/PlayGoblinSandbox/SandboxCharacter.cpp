#include "SandboxCharacter.h"

#include "Camera/CameraComponent.h"
#include "Components/InputComponent.h"
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

void ASandboxCharacter::SetupPlayerInputComponent(UInputComponent* In)
{
	Super::SetupPlayerInputComponent(In);
	In->BindAxis(TEXT("MoveForward"), this, &ASandboxCharacter::MoveForward);
	In->BindAxis(TEXT("MoveRight"), this, &ASandboxCharacter::MoveRight);
	In->BindAxis(TEXT("Turn"), this, &ASandboxCharacter::Turn);
	In->BindAction(TEXT("Jump"), IE_Pressed, this, &ACharacter::Jump);
}

void ASandboxCharacter::MoveForward(float V)
{
	if (V != 0.f && Controller) AddMovementInput(FRotationMatrix(FRotator(0, Controller->GetControlRotation().Yaw, 0)).GetUnitAxis(EAxis::X), V);
}

void ASandboxCharacter::MoveRight(float V)
{
	if (V != 0.f && Controller) AddMovementInput(FRotationMatrix(FRotator(0, Controller->GetControlRotation().Yaw, 0)).GetUnitAxis(EAxis::Y), V);
}

void ASandboxCharacter::Turn(float V)
{
	AddControllerYawInput(V);
}
