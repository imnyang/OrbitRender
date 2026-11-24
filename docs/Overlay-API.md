# Overlay API (삭제됨)

`OrbitRender.OverlayApi`는 [RenderApi v3](CSharp-API.md)에서 삭제되었습니다. 오버레이·키뷰어 모드는 더 이상 OrbitRender에 등록하지 않습니다.

## 리플레이 렌더

TUFReplay가 리플레이를 진행하고 OrbitRender는 렌더링만 합니다. 오버레이·키뷰어 모드는 **TUFReplay의 `ReplayApi`**로 연동하세요. TUFReplay가 녹화된 입력과 상태를 전달하고, 모드의 캔버스를 모아 OrbitRender가 캡처하도록 넘깁니다. 게임 밖에서 따로 그린 영상(예: 외부 앱의 키뷰어)은 `ReplayApi`를 통해 넘기면 TUFReplay가 [덧씌울 영상](CSharp-API.md#덧씌울-영상)으로 OrbitRender에 전달합니다. 연동 방법은 TUFReplay의 `ReplayApi` 문서를 참고하세요.

- `ReplayApi`를 쓰지 않는 오버레이·키뷰어 모드는 리플레이 렌더에서 지원하지 않습니다. 녹화된 키 입력을 SkyHook 이벤트나 `Input.GetKey`로 주입하지 않습니다.
- 그래서 리플레이 렌더도 일반 렌더와 같은 기준으로 모드 캔버스를 자동으로 찾습니다. RainingKeys, AdofaiTweaks 키뷰어처럼 키 입력을 보여 주는 캔버스는 빠지고, `ReplayApi` 모드가 넘긴 캔버스는 이름과 관계없이 들어갑니다.
- OrbitRender는 오버레이 모드의 시간(`Time`, `Stopwatch`, `DateTime`)만 영상 타임라인에 맞춥니다.

## 일반 렌더

내보내기 설정의 **모드 오버레이 표시**를 켜면 OrbitRender가 화면 오버레이 캔버스를 자동으로 찾아 캡처합니다. 등록은 필요 없습니다. JipperResourcePack의 플레이 횟수와 최고 기록은 렌더 중 바뀌지 않도록 보호합니다.

## 이전 provider 옮기기

`OverlayApi.Register`로 등록하던 provider는 다음처럼 옮깁니다.

| 이전 (`OverlayApi`) | 이후 |
| --- | --- |
| `GetOverlayCanvases()` | 리플레이: TUFReplay `ReplayApi`로 캔버스 전달. 일반 렌더: 자동 탐색 |
| `OnReplayClock`, `OnReplayKey`, `OnReplayInput`, `OnReplayState` | TUFReplay `ReplayApi` |
| `OnRenderSessionBegin`, `OnRenderSessionEnd`, `OnBeforeOverlayCapture`, `OnAfterOverlayCapture` | 리플레이: TUFReplay `ReplayApi`. 일반 렌더: 지원하지 않음 |
