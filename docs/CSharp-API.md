# C# API

다른 ADOFAI 모드에서 OrbitRender에 렌더링을 요청할 수 있습니다. `OrbitRender.dll`을 참조하지 않아도 되도록 공개 표면은 **문자열 인자와 JSON 문자열 반환값**으로만 구성됩니다. 리플렉션으로 `OrbitRender.RenderApi`를 찾아 호출하세요. 모든 호출은 Unity 메인 스레드에서 실행해야 합니다.

## 표면

```csharp
namespace OrbitRender
{
    public static class RenderApi
    {
        public const int ApiVersion = 2;   // 계약이 깨질 때 증가합니다.
        public static bool IsAvailable { get; }
        public static string Version { get; }
        public static bool IsBusy { get; }

        public static string StartReplay(string manifestPath, string optionsJson);
        public static string ReadStatus(string jobId);
        public static string Cancel(string jobId);
    }
}
```

`ApiVersion`이 2보다 작은 OrbitRender는 이 계약을 지원하지 않습니다. `StartReplay`를 호출하기 전에 `IsAvailable`과 `ApiVersion`을 확인하고, 지원하지 않으면 사용자에게 OrbitRender 2.7.0 이상 설치를 안내하세요.

## 렌더 시작

`StartReplay`는 TUFReplay가 만든 `manifest.json` 경로를 받아 **레벨 열기 → 렌더 시작 → 작업 등록**까지 OrbitRender가 담당합니다. 두 번째 인자는 선택 사항인 JSON 옵션이며, `null`이나 빈 문자열이면 OrbitRender의 현재 설정을 사용합니다.

```csharp
string options = @"{
  ""width"": 1920,
  ""height"": 1080,
  ""targetFps"": 240,
  ""videoFps"": 60,
  ""bitrateMbps"": 18,
  ""videoCodec"": ""H264"",
  ""bitDepth"": 8,
  ""captureAudio"": true,
  ""audioGainDb"": 0,
  ""endDelaySeconds"": 2,
  ""bgaMode"": false,
  ""showPlanetRings"": true,
  ""showSongTitle"": true,
  ""showCountdown"": true,
  ""showResultText"": true,
  ""showHitJudgments"": true,
  ""showModOverlay"": true
}";
string json = RenderApi.StartReplay(manifestPath, options);
```

옵션은 모두 선택 사항이며, 지정하지 않은 값은 OrbitRender 설정을 따릅니다.

| 항목 | 필드 |
| --- | --- |
| 화면과 프레임 | `preset`, `width`, `height`, `targetFps`, `videoFps`, `endDelaySeconds` |
| 영상 인코딩 | `bitrateMbps`, `videoCodec`, `bitDepth`, `audioCodec` |
| 오디오 | `captureAudio`, `audioGainDb` |
| 표시 | `bgaMode`, `showPlanetRings`, `showSongTitle`, `showCountdown`, `showResultText`, `showHitJudgments`, `showModOverlay` |

`width`, `height`, `targetFps`, `videoFps`, `bitrateMbps`, `bitDepth`, `videoCodec`, `audioCodec`는 범위를 벗어나면 `render_options_invalid`로 즉시 거부됩니다. `preset`은 `Custom`, `Preview`, `FullHD`, `QHD`, `UHD4K` 중 하나입니다.

## 응답

모든 메서드는 다음 중 하나의 JSON 봉투를 반환합니다. 예외는 경계를 넘지 않습니다.

```json
{ "ok": true, "job": { "id": "...", "state": "rendering", "progress": 0.42 } }
```

```json
{ "ok": false, "error": {
  "code": "level_file_missing",
  "message": "The recorded level file is missing.",
  "field": "level.path",
  "line": null,
  "file": "level.adofai"
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

`Cancel`은 취소를 요청하는 즉시 반환하며, 실제 종료는 이후 `ReadStatus`에서 `cancelled`로 확인합니다. 이미 끝난 작업을 취소하면 `render_job_finished` 오류가 반환됩니다.

## 오류 코드

| 코드 | 의미 |
| --- | --- |
| `renderer_unavailable` | OrbitRender가 비활성화되었거나 준비되지 않음 |
| `render_busy` | 이미 다른 렌더가 진행 중 |
| `render_manifest_missing` / `render_manifest_invalid` | 매니페스트가 없거나 읽을 수 없음 |
| `render_level_path_missing` / `render_level_path_invalid` | 레벨 경로가 없거나 잘못됨 |
| `render_level_missing` / `level_file_missing` | 레벨 파일이 없음 |
| `render_level_mismatch` | 매니페스트와 요청한 레벨이 다름 |
| `render_options_invalid` | 옵션 값이 범위를 벗어남 |
| `render_job_unknown` / `render_job_finished` | 작업 ID를 찾을 수 없거나 이미 종료됨 |
| `render_wrong_thread` | Unity 메인 스레드가 아닌 곳에서 호출함 |
| `render_internal_error` | OrbitRender 내부 예외 |

매니페스트 검증 실패는 TUFReplay 번들 계약의 코드(`render_metadata_*`, `render_input_payload_invalid`, `level_gameplay_modified` 등)를 그대로 전달하며, `field`, `line`, `file`로 문제 위치를 알려 줍니다.

## 작업 수명과 정리

- 한 번에 하나의 작업만 실행합니다.
- 종료된 작업의 상태는 1시간 동안 유지된 뒤 정리됩니다.
- 결과 파일은 호출자가 관리합니다. `outputPath`를 직접 읽고, 이동·삭제도 호출자 책임입니다.
- 호출자는 완료 후 `RenderBundles/<jobId>` 같은 임시 입력을 스스로 정리해야 합니다.

## TUFReplay 연동

TUFReplay는 이 API를 리플렉션으로 호출합니다. 브라우저는 TUFReplay의 ADOFAI IPC 계약만 사용하며 OrbitRender와 직접 통신하지 않습니다. 렌더 작업의 소유자(번들 생성, 상태 머신, 취소, 정리)는 TUFReplay이고, OrbitRender는 레벨 열기·렌더링·상태 보고만 담당합니다.
