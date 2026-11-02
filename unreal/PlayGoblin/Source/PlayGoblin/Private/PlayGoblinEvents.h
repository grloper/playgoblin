#pragma once

#include "CoreMinimal.h"

DECLARE_LOG_CATEGORY_EXTERN(LogPlayGoblin, Log, All);

namespace PlayGoblin
{
	inline FString JsonEscape(const FString& In)
	{
		FString Out;
		Out.Reserve(In.Len() + 8);
		for (TCHAR C : In)
		{
			switch (C)
			{
			case TEXT('"'): Out += TEXT("\\\""); break;
			case TEXT('\\'): Out += TEXT("\\\\"); break;
			case TEXT('\n'): Out += TEXT("\\n"); break;
			case TEXT('\r'): Out += TEXT("\\r"); break;
			case TEXT('\t'): Out += TEXT("\\t"); break;
			default:
				if (C < 0x20) Out += FString::Printf(TEXT("\\u%04x"), static_cast<int32>(C));
				else Out.AppendChar(C);
			}
		}
		return Out;
	}

	inline FString Num(double V) { return FString::SanitizeFloat(V); }

	/** Unreal is Z-up in centimetres. Run folders use metres and Y-up (X, height, Z) so the CLI reads them like Unity runs. */
	inline FString PosJson(const FVector& Cm)
	{
		return FString::Printf(TEXT("[%s,%s,%s]"), *Num(FMath::RoundToDouble(Cm.X) / 100.0),
			*Num(FMath::RoundToDouble(Cm.Z) / 100.0), *Num(FMath::RoundToDouble(Cm.Y) / 100.0));
	}

	inline FString MetresCsv(const FVector& Cm)
	{
		return FString::Printf(TEXT("%.2f,%.2f,%.2f"), Cm.X / 100.0, Cm.Z / 100.0, Cm.Y / 100.0);
	}

	/** FNV-1a, 8 hex digits: stable across runs, used to group identical errors. */
	inline FString StableHash(const FString& S)
	{
		uint32 H = 2166136261u;
		for (TCHAR C : S) { H ^= static_cast<uint32>(C); H *= 16777619u; }
		return FString::Printf(TEXT("%08x"), H);
	}
}

struct FPGEvent
{
	int64 Seq = 0;
	double T = 0.0;
	FString Type;
	FString Detail;
	bool bHasPos = false;
	FVector Pos = FVector::ZeroVector;
	TArray<TPair<FString, FString>> Data;

	FString ToJson() const
	{
		FString J = FString::Printf(TEXT("{\"seq\":%lld,\"t\":%s,\"type\":\"%s\""), Seq, *PlayGoblin::Num(FMath::RoundToDouble(T * 1000.0) / 1000.0),
			*PlayGoblin::JsonEscape(Type));
		if (!Detail.IsEmpty()) J += FString::Printf(TEXT(",\"detail\":\"%s\""), *PlayGoblin::JsonEscape(Detail));
		if (bHasPos) J += TEXT(",\"pos\":") + PlayGoblin::PosJson(Pos);
		if (Data.Num() > 0)
		{
			J += TEXT(",\"data\":{");
			for (int32 i = 0; i < Data.Num(); ++i)
			{
				if (i) J += TEXT(",");
				J += FString::Printf(TEXT("\"%s\":\"%s\""), *PlayGoblin::JsonEscape(Data[i].Key), *PlayGoblin::JsonEscape(Data[i].Value));
			}
			J += TEXT("}");
		}
		return J + TEXT("}");
	}
};
