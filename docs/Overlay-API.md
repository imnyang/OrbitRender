# Overlay API

키뷰어·상태 HUD 같은 오버레이 모드가 OrbitRender와 **이름 기반 탐색이나 사설 필드 패치 없이** 협력하기 위한 공개 API입니다.
OrbitRender 2.7.0 이상에서 사용할 수 있습니다.

## 타입 격리 규칙

어댑터의 필드, 파라미터, 반환형에 **상대 모드의 타입을 넣지 않습니다.**

- OrbitRender는 상대 모드 타입을 직접 참조하지 않고 `object` + 리플렉션으로만 호출합니다.
- 상대 모드는 `OrbitRender.dll`을 참조하지 않고 `OrbitRender.OverlayApi`를 이름으로 찾습니다.
- 경계를 넘는 타입은 `string`, `bool`, `IEnumerable<Canvas>` 같은 BCL/Unity 타입뿐입니다.
- 메서드 이름과 등록 id는 문자열 계약입니다.

이 규칙 덕분에 어느 한쪽 모드가 없거나 비활성화되어도 다른 쪽은 정상 동작합니다.

## 데이터 흐름

```text
TUFReplay
  └─ 녹화 DB → RenderBundles/<job>/manifest.json (레벨·리플레이·입력·판정)
       └─ OrbitRender.RenderApi v2 (리플렉션 + JSON)
            └─ RecordedReplayDriver
                 ├─ 게임에 입력/판정 적용
                 └─ OverlayApi
                      ├─ OnReplayClock(videoSeconds, ticks)
                      ├─ OnReplayInput(keyCode, down, ticks)
                      └─ 캡처 캔버스 / 전후 콜백
                           └─ 키뷰어·HUD 오버레이
```

- 시간은 리플레이 **영상 타임라인** 기준입니다. `VideoTicks`와 `OnReplayClock`/`OnReplayInput`의 `ticks`는 `TimeSpan.TicksPerSecond`(1초 = 10,000,000) 단위라 `Stopwatch` 기반 표시기와 그대로 맞출 수 있습니다.
- `OnReplayInput`의 `ticks`는 해당 입력이 발생한 시각이며, `ApplyInput` 동안에만 이벤트 시각으로 갱신된 뒤 프레임 시각으로 돌아갑니다.
- 등록된 provider는 OrbitRender의 기존 이름 기반 탐색·JRP 전용 보정에서 제외됩니다. provider가 있으면 JRP의 `JrpRenderGuard`/`jrpTicksGetter` 같은 사설 접근도 건너뜁니다.

## OrbitRender 표면

```csharp
namespace OrbitRender
{
    public static class OverlayApi
    {
        public const int ApiVersion = 1;   // 계약이 깨질 때 증가

        public static bool IsRendering { get; }
        public static bool IsReplay { get; }
        public static int Width { get; }
        public static int Height { get; }
        public static int VideoFps { get; }
        public static double VideoSeconds { get; }
        public static long VideoTicks { get; }

        public static bool Register(string id, object provider);
        public static void Unregister(string id);
        public static bool IsRegistered(string id);
    }
}
```

등록은 메인 스레드에서 하고, OrbitRender가 늦게 로드될 수 있으므로 실패하면 주기적으로 재시도하세요.

## Provider 계약

등록한 객체에서 아래 메서드를 **이름으로** 찾아 호출합니다. 모두 선택 사항입니다.

| 메서드 | 호출 시점 |
| --- | --- |
| `IEnumerable<Canvas> GetOverlayCanvases()` | 렌더 세션 시작. 캡처할 캔버스 목록 |
| `void OnRenderSessionBegin(bool replay)` | 렌더 준비 시작 |
| `void OnRenderSessionEnd()` | 렌더 정리(게임 복구 이후) |
| `void OnBeforeOverlayCapture(bool replay)` | 매 프레임 HUD 렌더 직전 |
| `void OnAfterOverlayCapture(bool replay)` | 매 프레임 HUD 렌더 직후 |
| `void OnReplayClock(double videoSeconds, long ticks)` | 리플레이 타임라인 갱신(매 프레임) |
| `void OnReplayInput(string keyCode, bool down, long ticks)` | 리플레이 렌더 중 기록된 키 입력 |

- `GetOverlayCanvases()`가 반환한 캔버스는 OrbitRender가 직접 캡처합니다. `showModOverlay` 설정과 무관하게 동작합니다.
- 일반적인 provider의 레이아웃은 해당 모드가 관리합니다. JipperResourcePack의 렌더 전용 배치와 레인 깊이 보정은 OrbitRender가 적용합니다.
- 리플레이 입력은 `KeyCode` 이름 문자열로 전달됩니다. 실시간 입력과 중복되지 않도록 어댑터에서 처리 방식을 정하세요.
- provider 콜백에서 예외가 나도 렌더는 중단되지 않고 로그만 남습니다.

## 예시 (JipperResourcePack)

```csharp
public static class OrbitRenderAPI
{
    private const string Id = "JipperResourcePack";
    private static MethodInfo _register;
    private static bool _registered;

    public static void Setup()
    {
        if (_registered) return;
        Type api = FindType("OrbitRender.OverlayApi");
        if (api == null) return;                    // 다음 프레임에 재시도
        _register = api.GetMethod("Register");
        _registered = _register.Invoke(null, new object[] { Id, new Provider() }) is true;
    }

    private sealed class Provider
    {
        public IEnumerable<Canvas> GetOverlayCanvases()
        {
            yield return Overlay.Instance.Canvas;                        // 상태 HUD
            yield return KeyViewer.Instance.KeyViewerObject
                .GetComponent<Canvas>();                                 // 키뷰어
        }

        public void OnReplayClock(double videoSeconds, long ticks)
        {
            _replayTicks = ticks;                    // 표시기 시계를 영상 시간으로
        }

        public void OnReplayInput(string keyCode, bool down, long ticks)
        {
            if (Enum.TryParse(keyCode, out KeyCode code))
                KeyViewer.Instance.ApplyReplayKey(code, down, ticks);
        }
    }
}
```

실제 적용 예시는 JipperResourcePack의 `KeyViewerContents/OtherModApi/OrbitRenderAPI.cs`와 LineKeyViewer의 `OrbitRenderAPI.cs`를 참고하세요. 두 파일 모두 상대 모드 타입을 전혀 참조하지 않습니다.
