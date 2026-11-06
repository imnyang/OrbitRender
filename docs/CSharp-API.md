# C# API

다른 ADOFAI 모드에서 OrbitRender에 렌더링을 요청할 수 있습니다. `OrbitRender.dll`을 참조하지 않아도 되도록 공개 표면은 **JSON 문자열 인자와 반환값**, 그리고 리플레이를 진행하는 **호스트 객체**로만 구성됩니다. 리플렉션으로 `OrbitRender.RenderApi`를 찾아 호출하세요. 모든 호출은 Unity 메인 스레드에서 실행해야 합니다.

출력 폴더 지원 여부는 `RenderApi.OutputDirectoryOptionVersion` 필드가 존재하고 값이 1 이상인지 리플렉션으로 확인할 수 있습니다.

## 표면

```csharp
namespace OrbitRender
{
    public static class RenderApi
    {
        public const int ApiVersion = 3;   // 계약이 깨질 때 증가합니다.
        public static bool IsAvailable { get; }
        public static string Version { get; }
        public static bool IsBusy { get; }

        public static string StartHostedReplay(string requestJson, object host);
        public static string ReadStatus(string jobId);
        public static string Cancel(string jobId);
    }
}
```

`ApiVersion` 3부터 `StartHostedReplay`를 사용합니다. 호출하기 전에 `IsAvailable`과 `ApiVersion`을 확인하고, 3보다 작으면 사용자에게 OrbitRender 업데이트를 안내하세요.

## 역할 분담

리플레이 렌더는 TUFReplay가 진행하고 OrbitRender는 렌더링만 합니다.

| OrbitRender | 호스트(TUFReplay) |
| --- | --- |
| 레벨 열기, 시작 타일 지정, 오프라인 시계와 프레임 루프 | 녹화된 입력과 판정, 이를 적용하는 게임플레이 훅 |
| 프레임 캡처, 오디오 캡처와 인코딩 | 녹화 당시 설정(피치, no-fail, input offset, 판정 난이도) 적용과 복원 |
| 웹캠·마이크 합성, 영상 파일 저장 | 종료·사망 처리, 렌더 종료 시각 결정 |
| 오버레이 모드의 시간 가상화(OptionalModClock) | 레벨 해시 확인, 오버레이·키뷰어 모드와의 연동 |

## 렌더 시작

`StartHostedReplay`는 요청 JSON을 검증하고, 호스트 멤버를 바인딩한 뒤, 레벨을 열고 렌더하는 작업을 등록합니다. 반환값은 아래 [응답](#응답)의 JSON 봉투입니다.

```csharp
Type api = FindType("OrbitRender.RenderApi");
if ((int)api.GetField("ApiVersion").GetValue(null) < 3) { /* 업데이트 안내 */ }
MethodInfo start = api.GetMethod("StartHostedReplay", new[] { typeof(string), typeof(object) });

string request = JsonConvert.SerializeObject(new {
    levelPath = @"C:\Levels\song\main.adofai",
    levelFileSha256 = "…64자리 16진수…",
    startTile = 0,
    gameInputOffsetMs = 12.0,
    wonTimeUs = 123456789L,
    media = new { webcam = new { path = @"C:\Recordings\abc\webcam.mkv", offsetMs = 125 } },
    warnings = new string[0],
    options = new { videoFps = 60, endDelaySeconds = 2, outputDirectory = @"C:\Renders" }
});
string json = (string)start.Invoke(null, new object[] { request, host });
```

### 요청 JSON

| 필드 | 형식 | 필수 | 설명 |
| --- | --- | --- | --- |
| `levelPath` | string | 예 | 레벨 파일의 절대 경로. OrbitRender가 편집기에서 엽니다. |
| `levelFileSha256` | string \| null | 아니요 | 64자리 16진수 SHA-256. 출력 파일 이름에만 씁니다. 없으면 레벨 파일에서 계산합니다. 레벨 변경 확인은 호스트가 합니다. |
| `startTile` | integer | 아니요 | 시작 타일. 기본값 0, 음수는 거부합니다. |
| `gameInputOffsetMs` | number | 예 | 녹화 당시 input offset(ms). 출력 오디오 지연으로만 씁니다. |
| `wonTimeUs` | integer \| null | 아니요 | 클리어 시각(μs, 녹화 기준). 웹캠·마이크 타임라인에만 씁니다. |
| `media` | object \| null | 아니요 | `webcam`, `microphone`. 녹화 매니페스트의 `media`와 같은 모양이며 `path`는 절대 경로여야 합니다. 없는 항목은 합성하지 않습니다. |
| `warnings` | string[] | 아니요 | 작업 스냅샷의 `warnings`에 그대로 표시합니다. |
| `options` | object \| null | 아니요 | 렌더 옵션. 아래 표를 참고하세요. |

알 수 없는 필드는 무시합니다. 미디어 파일은 요청 시점과 렌더 시작 시점에 다시 확인합니다. 파일이 없거나 비어 있거나 읽을 수 없으면 `render_media_file_*` 오류로 거부합니다.

### 렌더 옵션

`options`의 값은 모두 선택 사항이며, 지정하지 않은 값은 OrbitRender 설정을 따릅니다.

```json
{
  "width": 1920,
  "height": 1080,
  "outputDirectory": "C:\\Renders",
  "targetFps": 240,
  "videoFps": 60,
  "bitrateMbps": 18,
  "videoCodec": "H264",
  "bitDepth": 8,
  "captureAudio": true,
  "audioGainDb": 0,
  "endDelaySeconds": 2,
  "bgaMode": false,
  "showPlanetRings": true,
  "showSongTitle": true,
  "showCountdown": true,
  "showResultText": true,
  "showHitJudgments": true,
  "showModOverlay": true
}
```

`outputDirectory`는 해당 작업에만 적용되는 절대 폴더 경로입니다. `~/Videos/Renders`처럼 `~`를 현재 사용자의 홈 폴더로 사용할 수도 있습니다. 생략하거나 빈 문자열 또는 `null`로 지정하면 현재 출력 폴더 설정을 사용합니다. 폴더가 없으면 렌더링할 때 생성합니다. 잘못된 경로는 `render_options_invalid`로 거부됩니다. Windows 경로의 역슬래시는 JSON에서 `\\`로 이스케이프해야 합니다. 이 옵션은 C# API 전용이며 UMM 설정을 변경하지 않습니다.

| 항목 | 필드 |
| --- | --- |
| 저장 폴더 | `outputDirectory` |
| 완료 화면 | `noPreview` (`true`이면 렌더 완료 화면을 표시하지 않음) |
| 화면과 프레임 | `preset`, `width`, `height`, `targetFps`, `videoFps`, `endDelaySeconds` |
| 영상 인코딩 | `bitrateMbps`, `videoCodec`, `bitDepth`, `audioCodec`, `proResProfile` |
| 오디오 | `captureAudio`, `audioGainDb` |
| 표시 | `bgaMode`, `showPlanetRings`, `showSongTitle`, `showCountdown`, `showResultText`, `showHitJudgments`, `showModOverlay` |

`width`, `height`, `targetFps`, `videoFps`, `bitrateMbps`, `bitDepth`, `videoCodec`, `audioCodec`는 범위를 벗어나면 `render_options_invalid`로 즉시 거부됩니다. `preset`은 `Custom`, `Preview`, `FullHD`, `QHD`, `UHD4K` 중 하나입니다. 리플레이 렌더는 게임플레이 HUD를 항상 모두 표시합니다.

`Begin`에는 종료 지연이 전달되지 않습니다. 호스트가 종료 시각을 계산할 때 쓸 값은 `options.endDelaySeconds`로 직접 지정하세요. 생략하면 OrbitRender는 사용자 설정값을 쓰므로 호스트는 그 값을 알 수 없습니다.

## 호스트 계약

OrbitRender는 작업을 만들 때 `host`의 **public 인스턴스 멤버**를 이름과 시그니처로 한 번 찾고, `Delegate.CreateDelegate`로 델리게이트를 만들어 둡니다. 이후 호출, 특히 프레임마다의 호출에는 `MethodInfo.Invoke`를 쓰지 않습니다. 반환형과 매개변수 형식은 정확히 같아야 합니다. 경계를 넘는 형식은 `string`, 숫자, `bool`, `Func<>`/`Action<>`, `IEnumerable<Canvas>` 같은 BCL/Unity 형식뿐입니다.

멤버가 하나라도 없거나 시그니처가 다르면 `render_host_invalid`로 거부합니다. `message`에는 빠진 멤버의 시그니처를 모두 적고, `field`에는 첫 번째 멤버 이름을 넣습니다.

| 멤버 | 호출 시점 |
| --- | --- |
| `void PrepareBeforeRender()` | 레벨이 준비되고 `Time.captureFramerate`가 설정되고 OptionalModClock이 시작된 뒤, 곡 클립을 준비하기 전. 호스트는 녹화 설정을 적용하고 게임플레이 훅을 설치합니다. |
| `void Begin(int width, int height, int videoFps, Func<bool> isCancelled, Action<double> setEndTimeSeconds, Action<double> requestStopAfterSeconds)` | 음악이 예약되고 `conductor.songposition_minusi`가 0번 프레임에 맞춰진 뒤, 첫 프레임 전. `setEndTimeSeconds(videoSeconds)`는 렌더 종료 시각을, `requestStopAfterSeconds(tailSeconds)`는 현재 프레임을 포함하고 지정한 시간만큼 더 렌더한 뒤 종료하도록 요청합니다. 시간은 모두 0번 출력 프레임부터의 초입니다. |
| `void BeforeSimulationFrame(long frameIndex, double videoSeconds)` | 시뮬레이션 프레임마다, 게임이 그 프레임을 진행하기 직전. |
| `void AfterSimulationFrame(long frameIndex, double videoSeconds)` | 같은 프레임의 게임 `Update`가 모두 끝난 뒤 OrbitRender의 `LateUpdate`에서. 오버레이 정착과 캡처보다 먼저입니다. |
| `long CurrentVideoTimeUs { get; }` | OptionalModClock의 시간 원천. 오버레이 모드의 `Time`, `Stopwatch`, `DateTime` 호출이 이 값을 따릅니다. 이벤트를 처리하는 동안에는 그 이벤트의 정확한 시각으로 옮겨도 됩니다. |
| `bool TerminalReached { get; }` | 렌더 끝 검사. 마지막 프레임까지 `true`가 아니면 렌더는 실패합니다. |
| `long? GameplayStartVideoTimeUs { get; }` | 웹캠·마이크 합성 타임라인. 영상 인코딩이 끝난 뒤 한 번 읽습니다. |
| `double GameplayRate { get; }` | 위와 같음. |
| `IEnumerable<Canvas> GetCaptureCanvases()` | 캡처할 캔버스를 모을 때. `Begin` 직후 한 번 호출되므로, 오버레이·키뷰어 모드의 캔버스는 그때까지 준비해 두세요. |
| `void OnBeforeCapture()` / `void OnAfterCapture()` | 출력 프레임에 HUD 캔버스를 그릴 때마다 그 전후. |
| `void End(string finalState)` | 렌더 정리를 시작할 때. `finalState`는 `Completed`, `Failed`, `Cancelled` 같은 `RenderState` 이름입니다. |
| `void RestoreAfterRender()` | `End` 직후. 녹화 설정과 훅을 되돌립니다. |

### 호출 순서

```text
StartHostedReplay ─ 레벨 열기 ─ 인코더 확인
  └─ PrepareBeforeRender ─ 곡 준비, 플레이 시작, 음악 예약 ─ Begin ─ GetCaptureCanvases
       └─ 프레임마다: BeforeSimulationFrame → (게임 Update) → AfterSimulationFrame
                     → 오버레이 작업 정착 대기 → OnBeforeCapture → OnAfterCapture
            └─ End → RestoreAfterRender → 게임 상태 복원 → 편집기 복구
```

- 렌더가 시작되기 전에 작업이 끝나면(레벨 열기·인코더 확인 실패, 준비 중 취소) 호스트 멤버는 하나도 호출되지 않습니다. `ReadStatus`의 종료 상태로 판단하세요.
- `RestoreAfterRender`는 `PrepareBeforeRender`가 실패했거나 호출되기 전에 렌더가 실패해도 호출됩니다. `End`는 `Begin`이 호출된 경우에만 호출되며, `Begin`이 예외를 던졌을 때도 호출됩니다. 두 메서드는 중복 호출과 부분 준비 상태를 견뎌야 합니다.
- `ReadStatus`가 종료 상태를 보고할 때는 `End`, `RestoreAfterRender`, OrbitRender의 게임 상태 복원이 이미 끝났습니다. 편집기 복구는 그 뒤에도 이어질 수 있으며, 그동안 `IsBusy`는 `true`입니다.
- 오버레이 정착 대기는 OptionalModClock의 작업 펜스를 프레임마다 최대 2초 기다립니다. 호스트 호출은 없습니다.

### 호스트가 맡는 게임 상태

- `scrConductor.calibration_i`가 녹화 당시 값(`gameInputOffsetMs / 1000`)을 돌려주도록 하세요. OrbitRender는 리플레이에서 이 값으로 곡 위치를 0번 프레임에 맞춥니다.
- 피치, `noFail`, `scrConductor.currentPreset.inputOffset`, 판정 난이도는 호스트가 적용합니다. 편집기 재생이 피치를 되돌릴 수 있으므로 음악을 예약하기 전에 다시 적용하세요. OrbitRender는 리플레이에서 input offset을 덮어쓰지 않고, 플레이 시작 후 `RDC.auto`를 `false`로 둡니다.
- 판정 오차 미터: OrbitRender가 `Begin` 직전에 게임의 미터를 켭니다. 녹화된 판정의 결과가 `scrController.UpdateHitErrorMeter` 시점에 플레이어(`marginTracker.lastAddedHitMargin`, `midspinInfiniteMargin`)와 행성(`cachedAngle`, `targetExitAngle`), `RDC.auto`에 반영되어 있어야 합니다.
- JipperResourcePack 통계 보호(`JrpRenderGuard`)는 일반 렌더에만 적용됩니다. 리플레이에서는 호스트가 오버레이 모드의 기록을 보호하세요. OrbitRender의 게임 상태 복원과 편집기 복구는 `RestoreAfterRender` 뒤에 실행됩니다.
- OrbitRender가 쓰는 Harmony ID `KGH1113.TUFReplayRenderer.OptionalModClock`, `KGH1113.TUFReplayRenderer.HitErrorMeter`는 호스트에서 쓰지 마세요.

### 예외

- `PrepareBeforeRender`, `Begin`, `BeforeSimulationFrame`, `AfterSimulationFrame`의 예외는 렌더를 실패시키고, 예외 메시지가 작업 상태의 `errorMessage`에 들어갑니다. 예외에 public `string Code` 속성(소문자 snake_case)이 있으면 `errorCode`로 전달하고, `Field`, `Line`(`int?`), `File` 속성이 있으면 `errorDetails`에 함께 넣습니다.
- `GetCaptureCanvases`, `OnBeforeCapture`, `OnAfterCapture`의 예외는 로그에 남기고 계속합니다.
- 속성 getter는 예외를 던지지 않아야 합니다. `CurrentVideoTimeUs`는 오버레이 모드 코드 안에서 호출됩니다.

## 응답

모든 메서드는 다음 중 하나의 JSON 봉투를 반환합니다. 예외는 경계를 넘지 않습니다.

```json
{ "ok": true, "job": { "id": "...", "state": "rendering", "progress": 0.42 } }
```

```json
{ "ok": false, "error": {
  "code": "render_media_file_missing",
  "message": "A selected recorded media file is missing. Export the recording again or disable that media before rendering.",
  "field": "media.webcam.path",
  "line": null,
  "file": "C:\\Recordings\\abc\\webcam.mkv"
} }
```

`job` 스냅샷 필드는 `id`, `state`, `progress`, `capturedFrames`, `totalFrames`, `outputPath`, `warnings`, `waitingForGameFocus`, `errorCode`, `errorMessage`, `errorDetails`, `message`, `levelPath`, `updatedUtc`입니다.

`state`는 다음 중 하나입니다.

| 상태 | 의미 |
| --- | --- |
| `queued` / `loading` | 작업이 등록되었고 레벨을 여는 중 |
| `preparing` | 렌더 준비(설정·인코더 확인) |
| `rendering` | 프레임 캡처 중 |
| `finishing` | 오디오·영상 합성 중 |
| `awaitingconfirmation` | 게임 창에서 인코더 대체 확인을 기다리는 중. `waitingForGameFocus`가 `true`입니다. |
| `completed` | 완료. `outputPath`에 결과 파일이 있습니다. |
| `failed` | 실패. `errorCode`, `errorMessage`, `errorDetails`를 확인하세요. |
| `cancelled` | 취소됨 |

`progress`는 0..1이며 `totalFrames`가 0이면 0입니다.

## 상태 확인과 취소

```csharp
string status = RenderApi.ReadStatus(jobId);   // 동일한 봉투
string cancelled = RenderApi.Cancel(jobId);    // 취소 요청 후의 스냅샷
```

`Cancel`은 취소를 요청하는 즉시 반환하며, 실제 종료는 이후 `ReadStatus`에서 `cancelled`로 확인합니다. 렌더 중에는 `Begin`에 전달한 `isCancelled`가 `true`가 됩니다. 이미 끝난 작업을 취소하면 `render_job_finished` 오류가 반환됩니다.

## 오류 코드

| 코드 | 의미 |
| --- | --- |
| `renderer_unavailable` | OrbitRender가 비활성화되었거나 준비되지 않음 |
| `render_busy` | 이미 다른 렌더가 진행 중 |
| `render_request_invalid` | 요청 JSON이 잘못되었거나 필드 형식이 틀림. `field`에 필드 이름이 있습니다. |
| `render_host_invalid` | 호스트가 없거나 필요한 멤버가 없거나 시그니처가 다름 |
| `render_level_path_missing` / `render_level_path_invalid` | 레벨 경로가 없거나 절대 경로가 아님 |
| `render_level_missing` / `level_file_missing` | 레벨 파일이 없음 |
| `level_fingerprint_invalid` | `levelFileSha256`이 64자리 16진수가 아님 |
| `render_start_tile_invalid` | `startTile`이 음수 |
| `render_media_file_invalid` / `render_media_file_missing` / `render_media_file_unreadable` | 미디어 경로가 절대 경로가 아니거나 파일이 비어 있음 / 파일이 없음 / 읽을 수 없음. `field`는 `media.webcam.path` 같은 위치, `file`은 경로입니다. |
| `render_options_invalid` | 옵션 값이 범위를 벗어남 |
| `render_job_unknown` / `render_job_finished` | 작업 ID를 찾을 수 없거나 이미 종료됨 |
| `render_wrong_thread` | Unity 메인 스레드가 아닌 곳에서 호출함 |
| `render_internal_error` | OrbitRender 내부 예외 |

렌더 중 호스트가 던진 예외는 위 [예외](#예외) 규칙대로 호스트의 코드를 그대로 전달합니다.

## 작업 수명과 정리

- 한 번에 하나의 작업만 실행합니다.
- 종료된 작업의 상태는 1시간 동안 유지된 뒤 정리됩니다.
- 결과 파일은 호출자가 관리합니다. `outputPath`를 직접 읽고, 이동·삭제도 호출자 책임입니다.
- `media` 파일은 렌더 마지막 단계에서 읽으므로 작업이 종료 상태가 된 뒤에 정리하세요.

## v2에서 바뀐 점

- `StartReplay(manifestPath, optionsJson)`는 삭제되었습니다. `StartHostedReplay(requestJson, host)`를 사용하세요.
- 녹화 번들 읽기, 레벨 해시 확인, 입력·판정 재생, 녹화 설정, 키보드 호환 패치, 오버레이 모드 연동은 TUFReplay로 옮겨졌습니다.
- `OrbitRender.OverlayApi`는 삭제되었습니다. 오버레이·키뷰어 모드는 TUFReplay의 `ReplayApi`로 연동합니다. [Overlay API 안내](Overlay-API.md)를 참고하세요.
- RPC와 ADOFAI IPC의 `replayManifestPath` 옵션은 `render_replay_manifest_unsupported`로 거부됩니다.

## 오디오 지연

리플레이 렌더는 판정을 그대로 재현하려고 녹화 당시의 input offset(`gameInputOffsetMs`)을 사용합니다. ADOFAI는 행성을 이 값만큼 믹싱된 소리보다 늦게 그리므로, OrbitRender는 게임 소리와 마이크를 포함한 출력 오디오 전체를 같은 값만큼 늦춰 화면에 맞춥니다. 마이크의 `latencyUs`는 게임 소리를 기준으로 계산하면 됩니다.
