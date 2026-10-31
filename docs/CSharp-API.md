# C# API

다른 ADOFAI 모드에서 OrbitRender를 직접 호출할 수 있습니다. 모드 프로젝트에 `OrbitRender.dll`을 참조하고, 게임에도 OrbitRender를 설치하고 활성화하세요. 이 API는 Unity 메인 스레드에서 호출해야 합니다.

## 렌더 시작과 상태 확인

에디터에서 현재 열린 레벨을 렌더링합니다. `TryStart`가 `true`를 반환하면 준비가 시작된 것입니다. 실제 완료 여부는 이후 상태로 확인하세요.

```csharp
using OrbitRender;
using OrbitRender.Renderer;

bool started = RenderApi.TryStart(new RenderRequestOptions
{
    Width = 1920,
    Height = 1080,
    TargetFps = 120,
    VideoFps = 60,
    CaptureAudio = true
});

// Unity Update 등에서 확인합니다.
var renderer = RenderApi.Controller;
if (renderer != null)
{
    RenderState state = renderer.State;
    long captured = renderer.CapturedFrames;
    long total = renderer.TotalFrames;
    string message = renderer.Message;
    string outputPath = renderer.OutputPath;
}
```

`RenderApi.Controller`는 OrbitRender가 비활성화되었거나 렌더러가 아직 준비되지 않으면 `null`입니다. `TryStart`는 렌더러를 사용할 수 없거나 사용 중이거나 준비를 시작하지 못하면 `false`를 반환합니다. 실패 원인은 렌더러가 있을 때 `Message`를 확인하세요. 반환값이 `true`여도 이후 준비 또는 렌더링이 실패할 수 있습니다.

상태는 `Idle`, `Preparing`, `Rendering`, `Finishing`, `Completed`, `Failed`, `Cancelled`, `AwaitingConfirmation`입니다. `AwaitingConfirmation`에서는 OrbitRender 화면에서 인코더 대체 여부를 확인해야 합니다. `Busy`는 진행 중인 작업이 있는지 나타냅니다. `TotalFrames`가 0일 수 있으므로 진행률 계산 시 확인하세요.

```csharp
// 진행 중인 렌더의 취소를 요청합니다.
bool cancellationRequested = RenderApi.TryCancel();
```

`TryCancel`은 취소 요청을 전달하면 `true`를 반환합니다. 실제 취소 완료는 `State`에서 확인하세요.

## 렌더 옵션

`RenderRequestOptions`의 값은 모두 선택 사항입니다. 지정하지 않은 값은 OrbitRender의 현재 설정을 따릅니다. 옵션 객체는 시작 시 복사되며 전역 설정을 변경하지 않습니다. `TryStart()` 또는 `TryStart(null)`로 현재 설정을 사용할 수도 있습니다.

| 항목 | 필드 |
| --- | --- |
| 출력 | `OutputDirectory`, `FileNameFormat`, `OpenOutputFolder` |
| 화면과 프레임 | `Preset`, `Width`, `Height`, `TargetFps`, `VideoFps`, `EndDelaySeconds` |
| 영상 인코딩 | `BitrateMbps`, `VideoCodec`, `BitDepth`, `Encoding`, `Encoder`, `Container` |
| 오디오 | `CaptureAudio`, `AudioGainDb`, `AudioCodec` |
| 표시 | `ShowRenderPreview`, `BgaMode`, `ShowPlanetRings`, `ShowSongTitle`, `ShowCountdown`, `ShowResultText`, `ShowHitJudgments`, `ShowModOverlay` |
| 렌더링 자원 | `UseRustRenderer`, `BufferPolicy` |
| 선택 구간 | `SelectionStartTile`, `SelectionEndTile` |
| 리플레이 | `ReplayManifestPath` |

선택 구간을 지정할 때는 시작과 끝 타일을 함께 지정하세요. 한쪽만 지정하면 선택 구간이 적용되지 않습니다. 프리셋과 개별 옵션을 함께 지정하면 개별 옵션이 해당 설정을 덮어씁니다.

## TUFReplay 번들 렌더

`ReplayManifestPath`에 TUFReplay가 만든 `manifest.json` 경로를 지정하면 번들에 기록된 리플레이를 렌더링합니다. 에디터에 기록 당시의 같은 레벨이 열려 있어야 하며, 번들의 `level.path`와 현재 레벨 파일이 다르면 렌더를 시작하지 않습니다. 레벨 파일은 접근 가능해야 합니다.

```csharp
bool started = RenderApi.TryStartReplay(
    @"C:\...\RenderBundles\<jobId>\manifest.json",
    new RenderRequestOptions { ShowModOverlay = true });
```

TUFReplay 동반 앱은 OrbitRender의 `orbitrender` IPC 계약으로 같은 작업을 수행합니다. TUFReplay에서 기록을 선택하면 번들을 준비한 뒤 `createRender`에 `replayManifestPath`를 전달하고, `renderStatus`로 진행률과 완료 경로를 확인합니다. OrbitRender가 설치·활성화되어 있지 않으면 TUFReplay는 기존 녹화·리플레이 기능만 사용합니다.
