#include "Containers/Ticker.h"
#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "HttpPath.h"
#include "HttpServerModule.h"
#include "HttpServerResponse.h"
#include "IHttpRouter.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "PlayGoblinRun.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

/**
 * Nothing runs unless the game is started with -playgoblin, so shipping a build with the plugin enabled is harmless.
 *
 *   -playgoblin                 start the local API on 127.0.0.1:47321 (override with -PlayGoblinPort=)
 *   -PlayGoblinSeconds=120      also start a run by itself once a game world exists
 *   -PlayGoblinSeed= -PlayGoblinOut=<folder> -PlayGoblinTimeScale= -PlayGoblinFpsBudgetMs= -PlayGoblinExit
 */
class FPlayGoblinModule : public IModuleInterface
{
public:
	virtual void StartupModule() override
	{
		if (!FParse::Param(FCommandLine::Get(), TEXT("playgoblin"))) return;
		int32 Port = 47321;
		FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinPort="), Port);
		StartServer(Port);

		float Seconds = 0.f;
		if (FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinSeconds="), Seconds) && Seconds > 0.f)
		{
			Auto.Minutes = Seconds / 60.f;
			FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinSeed="), Auto.Seed);
			FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinOut="), Auto.OutputRoot);
			FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinTimeScale="), Auto.TimeScale);
			FParse::Value(FCommandLine::Get(), TEXT("PlayGoblinFpsBudgetMs="), Auto.FpsBudgetMs);
			Auto.bExitWhenDone = FParse::Param(FCommandLine::Get(), TEXT("PlayGoblinExit"));
			bAutoPending = true;
		}
		Ticker = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateRaw(this, &FPlayGoblinModule::Tick));
	}

	virtual void ShutdownModule() override
	{
		if (Ticker.IsValid()) FTSTicker::GetCoreTicker().RemoveTicker(Ticker);
		Current.Reset();
	}

private:
	FTSTicker::FDelegateHandle Ticker;
	TUniquePtr<FPlayGoblinRun> Current;
	FPGSnapshot LastSnapshot;
	FString LastSummaryPath;
	FPlayGoblinOptions Auto;
	bool bAutoPending = false;

	static UWorld* FindGameWorld()
	{
		if (!GEngine) return nullptr;
		for (const FWorldContext& C : GEngine->GetWorldContexts())
			if ((C.WorldType == EWorldType::Game || C.WorldType == EWorldType::PIE) && C.World() && C.World()->HasBegunPlay())
				return C.World();
		return nullptr;
	}

	bool Tick(float)
	{
		UWorld* World = FindGameWorld();
		if (!World) return true;
		if (bAutoPending)
		{
			bAutoPending = false;
			StartRun(Auto);
		}
		if (Current)
		{
			Current->Tick(World);
			if (Current->GetState() == EPGState::Ended)
			{
				LastSnapshot = Current->Snapshot();
				LastSummaryPath = FPaths::Combine(Current->RunDirectory, TEXT("run.json"));
				const bool bExit = Current->bExitWhenDone();
				Current.Reset();
				if (bExit) FPlatformMisc::RequestExit(false);
			}
		}
		return true;
	}

	void StartRun(const FPlayGoblinOptions& Options)
	{
		if (Current) Current->Stop();
		Current = MakeUnique<FPlayGoblinRun>(Options);
		Current->Begin();
	}

	static FString StateName(EPGState S)
	{
		switch (S)
		{
		case EPGState::Starting: return TEXT("starting");
		case EPGState::Running: return TEXT("running");
		case EPGState::Ended: return TEXT("ended");
		default: return TEXT("idle");
		}
	}

	FString StatusJson()
	{
		const FPGSnapshot S = Current ? Current->Snapshot() : LastSnapshot;
		using namespace PlayGoblin;
		return FString::Printf(TEXT("{\"state\":\"%s\",\"run_id\":\"%s\",\"run_dir\":\"%s\",\"end_reason\":\"%s\",\"t\":%s,\"events\":%lld,\"stuck\":%d,\"out_of_world\":%d,\"errors\":%d,\"coverage\":%s}"),
			*StateName(S.State), *JsonEscape(S.RunId), *JsonEscape(S.RunDir), *JsonEscape(S.EndReason), *Num(S.T), S.Events, S.Stucks, S.Falls, S.Errors, *Num(S.Coverage));
	}

	static TUniquePtr<FHttpServerResponse> Json(const FString& Body, EHttpServerResponseCodes Code = EHttpServerResponseCodes::Ok)
	{
		TUniquePtr<FHttpServerResponse> R = FHttpServerResponse::Create(Body, TEXT("application/json"));
		R->Code = Code;
		return R;
	}

	void StartServer(int32 Port)
	{
		TSharedPtr<IHttpRouter> Router = FHttpServerModule::Get().GetHttpRouter(Port, true);
		if (!Router.IsValid())
		{
			UE_LOG(LogPlayGoblin, Error, TEXT("Could not open port %d for the PlayGoblin API."), Port);
			return;
		}
		auto Bind = [&](const TCHAR* Path, EHttpServerRequestVerbs Verb, TFunction<TUniquePtr<FHttpServerResponse>(const FHttpServerRequest&)> Fn)
		{
			Router->BindRoute(FHttpPath(Path), Verb, FHttpRequestHandler::CreateLambda(
				[Fn](const FHttpServerRequest& Req, const FHttpResultCallback& Done) { Done(Fn(Req)); return true; }));
		};
		Bind(TEXT("/status"), EHttpServerRequestVerbs::VERB_GET, [this](const FHttpServerRequest&) { return Json(StatusJson()); });
		Bind(TEXT("/events"), EHttpServerRequestVerbs::VERB_GET, [this](const FHttpServerRequest& Req)
		{
			const FString* SinceS = Req.QueryParams.Find(TEXT("since"));
			const FString* MaxS = Req.QueryParams.Find(TEXT("max"));
			const int64 Since = SinceS ? FCString::Atoi64(**SinceS) : 0;
			const int32 Max = MaxS ? FMath::Clamp(FCString::Atoi(**MaxS), 1, 5000) : 1000;
			return Json(Current ? Current->EventsJson(Since, Max) : TEXT("{\"events\":[],\"next\":0}"));
		});
		Bind(TEXT("/runs/latest"), EHttpServerRequestVerbs::VERB_GET, [this](const FHttpServerRequest&)
		{
			FString Text;
			if (LastSummaryPath.IsEmpty() || !FFileHelper::LoadFileToString(Text, *LastSummaryPath))
				return Json(TEXT("{\"error\":\"no_run\"}"), EHttpServerResponseCodes::NotFound);
			return Json(Text);
		});
		Bind(TEXT("/run/start"), EHttpServerRequestVerbs::VERB_POST, [this](const FHttpServerRequest& Req)
		{
			FPlayGoblinOptions O;
			FString Body = FString(UTF8_TO_TCHAR(reinterpret_cast<const char*>(Req.Body.GetData()))).Left(Req.Body.Num());
			TSharedPtr<FJsonObject> Obj;
			if (!Body.IsEmpty() && FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Body), Obj) && Obj.IsValid())
			{
				Obj->TryGetNumberField(TEXT("seed"), O.Seed);
				double Minutes = O.Minutes;
				Obj->TryGetNumberField(TEXT("minutes"), Minutes);
				O.Minutes = static_cast<float>(Minutes);
			}
			if (O.Minutes <= 0.f) return Json(TEXT("{\"error\":\"minutes must be > 0\"}"), EHttpServerResponseCodes::BadRequest);
			StartRun(O);
			return Json(TEXT("{\"state\":\"starting\"}"), EHttpServerResponseCodes::Accepted);
		});
		Bind(TEXT("/run/stop"), EHttpServerRequestVerbs::VERB_POST, [this](const FHttpServerRequest&)
		{
			if (Current) Current->Stop();
			return Json(StatusJson());
		});
		FHttpServerModule::Get().StartAllListeners();
		UE_LOG(LogPlayGoblin, Display, TEXT("PlayGoblin API listening on port %d"), Port);
	}
};

IMPLEMENT_MODULE(FPlayGoblinModule, PlayGoblin)
