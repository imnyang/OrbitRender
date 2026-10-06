# OrbitRender

ADOFAI 커스텀 레벨을 지정한 해상도와 FPS로 영상으로 렌더링하는 Unity Mod Manager 모드입니다.

## 언어별 문서

- [한국어](README.md)
- [English](README.en.md)
- [JavaScript](README.js)
- [RPC API Specification](docs/RPC_API.md)

## 주요 기능

- 에디터 파일 메뉴의 `Export Video` 또는 `F6`으로 현재 열린 커스텀 레벨 렌더링
- `Export Video` 실행 전 해상도, FPS, 비트레이트, 오디오, BGA, 코덱, 인코더 등을 확인·변경하는 설정창
- 중앙 진행창, 렌더 FPS, 실시간 배율, ETA와 완료 예정 시각 표시
- Preview, FullHD, QHD, UHD 4K, Custom 프로필
- 해상도, Target FPS 15–1024 / Video FPS 15–240, 1–200 Mbps 비트레이트, End delay 설정
- H.264/AVC, H.265/HEVC, VP9, AV1 코덱 선택 지원 (자동 출력은 VP9 WebM, 나머지 MP4; MP4/TS/MKV/MOV 직접 선택 가능)
- NVIDIA NVENC, Intel Quick Sync, AMD AMF, 소프트웨어 인코더 드롭다운 선택 및 FFmpeg 실제 인코딩으로 사용 가능 여부 확인
- 게임 오디오 캡처와 영상·오디오 mux
- 설정 가능한 출력 폴더
- BGA Mode: 타일·홀드·타일 이펙트·공·공 파티클·힛사운드 제외
- localhost RPC API 및 `bgaMode` 작업별 override

## 설치

모드를 받은 뒤에 `A Dance of Fire and Ice/Mods/`에 이쁘게 배치해주세요.

Windows, macOS, Linux에서 실행할 수 있도록 플랫폼별 ADOFAI/Unity Mod Manager 환경만 준비해주세요. FFmpeg가 없으면 모드 첫 실행 시 설치 여부를 확인하며, 동의하면 인터넷을 통해 현재 플랫폼에 맞는 FFmpeg를 `FFmpeg/<platform>/`에 설치합니다. 이미 설치되어 있거나 `FFmpeg executable` 설정에 경로를 지정한 경우에는 확인 없이 해당 파일을 사용합니다.

빌드 산출물에는 FFmpeg가 포함되지 않습니다. 설치에 동의하면 모드가 Windows는 `windows-x64`, Linux는 `linux-x64`, Intel Mac은 `macos-x64`, Apple Silicon은 `macos-arm64`를 자동으로 선택해 해당 바이너리 하나만 다운로드합니다.

## 자동 업데이트

실행할 때 GitHub의 최신 정식 릴리즈를 확인합니다. `releases/latest` 기준으로 draft와 Pre-release는 자동 업데이트 대상에서 제외하며, 다운로드한 ZIP의 버전과 SHA-256을 확인합니다. 렌더 중이 아닐 때 Unity Mod Manager를 hot-reload하여 게임을 재시작하지 않고 적용하고, hot-reload가 지원되지 않으면 게임 종료 후 적용합니다.

## 설정

UMM 설정과 게임 내 내보내기 창은 **기본 / 게임 설정 / 고급** 탭으로 구성됩니다. 프리셋·인코딩 속도·비트 깊이는 라디오 선택을 사용하며, 파일명 예시와 파일명 형식·출력 형식은 탭 아래에 항상 표시합니다. 변수 삽입과 문법 설명은 **변수 도움말**에서 열 수 있습니다. UMM 전용 경로 설정·FFmpeg 설치·진단·초기화는 고급 탭의 파일 및 문제 해결 항목에 있습니다.

| 설정 | 기본값 | 설명 |
| --- | --- | --- |
| Preset | FullHD | Preview / FullHD / QHD / UHD 4K / Custom |
| Width / Height | 1920 × 1080 | Custom 해상도, 짝수로 보정 |
| Target FPS | 60 | 인게임/게임 시뮬레이션 업데이트 FPS, 15–1024 |
| Video FPS | 60 | 최종 출력 영상 FPS, 15–240. Target FPS와 독립적으로 설정 |
| Video bitrate | 18 Mbps | 1–200 Mbps CBR |
| End delay | 2초 | 음악 또는 마지막 타일 이후 대기 |
| Capture audio | 켜짐 | 게임 음악/오디오 캡처 |
| 오디오 음량 조절 | 0 dB | 캡처 오디오를 최종 영상에 합칠 때 적용, -60~12 dB |
| 렌더 중 미리보기 표시 | 켜짐 | 렌더 중 게임 화면에 출력 프레임 표시 |
| BGA mode | 꺼짐 | 타일, 공, 힛사운드 없이 렌더 |
| Show planet rings | 켜짐 | 행성 궤도 링 포함 |
| Show song title | 켜짐 | 기본 곡 제목 텍스트 포함 |
| Show countdown | 켜짐 | 준비, 카운트다운 숫자, 시작 텍스트 포함 |
| Show result text | 켜짐 | 완료/Pure Perfect 문구만 포함 (세부 판정 결과는 숨김) |
| Show hit judgments | 꺼짐 | 타일을 밟을 때 판정 텍스트 표시 |
| Encoding speed | Quality | Maximum / Balanced / Quality |
| Video encoder | Auto (H.264) | 인코더와 코덱을 함께 선택 (NVENC H.264, x265 HEVC, AOM AV1 등) |
| Video bit depth | 8-bit | 8-bit / 10-bit (`yuv420p10le`) |
| Output format | Auto | Auto / .mp4 / .ts / .mkv / .mov |
| Filename format | `Render_{level}_{date}_{time}_{id}` | 레벨명, 날짜, 시간, 랜덤 ID; 확장자 자동 추가 |
| Output folder | `Renders` | 게임 폴더 기준 상대 경로 또는 절대 경로 |
| Open output folder after render | 켜짐 | 렌더 완료 후 결과 파일이 있는 폴더 열기 |
| FFmpeg executable | 자동 | 첫 실행 동의 후 설치된 FFmpeg, PATH의 `ffmpeg`, 또는 직접 지정한 경로 |

AV1 소프트웨어 인코더는 엄격한 CBR을 지원하지 않으므로 오디오 포함 렌더에서는 목표 비트레이트 VBR, 무음 렌더에서는 capped-CRF를 사용합니다.

VAAPI도 H.264, HEVC, VP9, AV1을 지원합니다. FFmpeg 기본 VAAPI 장치를 초기화하고 8-bit는 NV12, 10-bit는 P010으로 변환한 뒤 GPU에 업로드합니다. FFmpeg 빌드와 GPU 드라이버가 해당 코덱을 지원하여 실제 인코딩 검사에 성공한 항목만 표시하며, 실패 시 기존 소프트웨어 fallback 확인을 사용합니다. 드라이버별 화질·rate control은 자동 선택합니다. [FFmpeg VAAPI 문서](https://ffmpeg.org/ffmpeg-codecs.html#VAAPI-encoders)

파일명이 중복되면 `_1`, `_2` 등의 번호를 붙이며 파일명에 쓸 수 없는 문자는 정리합니다. 인코더 목록에는 현재 비트 심도와 출력 형식으로 실제 인코딩에 성공한 항목만 표시합니다. FFmpeg나 드라이버를 바꾼 뒤에는 **인코더 다시 확인**을 누르세요. TS는 H.264/H.265를 지원하며, 나머지 코덱·컨테이너 조합의 사용 가능 여부도 FFmpeg로 확인합니다.

렌더 시작 전에 선택한 인코더를 실제 1프레임으로 점검합니다. 하드웨어 인코더가 실패하면 Software encoder로 이번 렌더만 계속할지 확인하며, 동의하지 않으면 설정과 렌더를 변경하지 않고 취소합니다.

### 파일명 템플릿

기존 `Render_{level}_{date}_{time}_{id}` 형식을 그대로 사용할 수 있습니다. 내보내기 창의 **변수 삽입** 메뉴에서 변수와 가공 예시를 넣고, 파일명 예시로 결과를 확인하세요. 잘못된 변수·문법은 오류로 표시하며 수정하기 전에는 내보내기를 시작하지 않습니다.

`{level}`은 레벨 설정의 곡 제목을 사용하며, 제목이 비어 있으면 레벨 파일 이름을 사용합니다. 형식에 `/` 또는 `\`를 넣으면 출력 폴더 아래에 하위 폴더를 자동 생성합니다. 예를 들어 `{level}/Render_{date:yyyy-MM-dd_HH-mm-ss}_{id}.mp4`는 `wowcoollevel/Render_2026-10-05_15-05-42_8e5bb0.mp4`처럼 저장됩니다. 변수 값에 포함된 슬래시는 `_`로 바뀌며, `.`과 `..` 경로 요소는 무시됩니다.

| 변수 | 값 |
|---|---|
| `{level}`, `{artist}` | 레벨명, 레벨에 저장된 아티스트명 (없으면 빈 문자열) |
| `{date}`, `{time}`, `{id}` | 날짜, 시간, 랜덤 ID |
| `{width}`, `{height}`, `{bitrate}` | 출력 해상도, 비트레이트 (Mbps) |
| `{videoFps}`, `{ingameFps}` | 영상 FPS, 인게임 FPS |
| `{codec}`, `{bitDepth}`, `{bgaMode}` | H264/H265/VP9/AV1, 8/10, true/false |

```text
{level}_{date:yyyyMMdd}_{width}x{height}_{videoFps}fps
{artist|default:"Unknown"} - {level|replace:"/","_"|truncate:40}
{level}_{if:bgaMode,"BGA","Gameplay"}
```

날짜·시간에는 `{date:yyyy-MM-dd}`, `{time:HHmmss}`처럼 .NET 날짜 형식을 지정할 수 있습니다. `|lower`, `|upper`, `|trim`, `|replace:"검색","치환"`, `|truncate:40`, `|default:"빈 값 대체"`를 순서대로 연결하면 문자열을 가공합니다. `truncate`는 0~160 사이의 길이를 받습니다. 문자열 인수는 큰따옴표로 감싸고, 큰따옴표와 역슬래시는 각각 `\"`, `\\`로 입력합니다.

`{if:bgaMode,"참일 때","거짓일 때"}`는 조건에 따라 문자열을 선택합니다. `!bgaMode`처럼 조건을 반전할 수 있고, `{if:artist,"있음","없음"}`처럼 문자열이 비어 있는지도 검사할 수 있습니다. 실제 중괄호는 `{{`, `}}`로 입력합니다. 변수 이름은 대소문자를 구분합니다.

확장자는 선택한 출력 형식에 따라 자동으로 붙습니다. 형식 끝에 `.mp4`, `.ts`, `.mkv`, `.mov`, `.webm`을 직접 적어도 중복되지 않습니다. 문자열 가공 후 파일명에 쓸 수 없는 문자를 정리하고 각 경로 요소를 최대 160자로 제한하며, 중복 파일명에는 번호를 붙입니다. 예시에 표시되는 날짜·시간·ID는 참고용이며 실제 내보내기 때 다시 생성됩니다. 템플릿은 최대 4096자입니다.

### 진단

설정 화면의 `Run diagnostics` 버튼으로 렌더 전에 다음 항목을 확인할 수 있습니다.

- FFmpeg 실행 가능 여부와 버전
- 선택한 비디오 인코더 지원 여부
- 선택한 비디오·오디오 코덱과 컨테이너 지원 여부
- 선택한 인코더의 실제 1프레임 smoke test와 GPU 드라이버 정보
- 출력 폴더 생성 및 쓰기 권한
- Unity 오디오 출력과 GPU readback 상태

`Copy report`로 진단 결과를 클립보드에 복사해 문제를 신고할 때 첨부할 수 있습니다. 진단은 임시 파일을 출력 폴더에 만들었다가 즉시 삭제합니다.

### BGA Mode

BGA Mode는 렌더 시작 시 씬의 원래 표시 상태를 저장하고, 렌더 직전에 다음 요소를 숨깁니다.

- 타일, 홀드, glow/icon/outline, 멀티플래닛 라인
- 공과 공의 trail/particle/특수 외형
- 타일 게임플레이 이펙트
- 힛, 홀드, 미드스핀 힛사운드

배경, 카메라, 장식, 음악 타이밍은 유지합니다. 렌더 성공·취소·실패 후에는 원래 렌더러 상태를 복원합니다. 음악까지 제외하려면 `Capture audio`도 끄세요.

## RPC

ADOFAI 실행 옵션에 다음을 추가하면 RPC 서버가 열립니다.

```text
--renderer-rpc
```

기본 주소는 `http://127.0.0.1:1108/`입니다. 상세한 엔드포인트, 요청/응답 스키마, 오류 코드, JavaScript 예제는 [docs/RPC_API.md](docs/RPC_API.md)를 참고하세요.

간단한 요청 예시:

```js
const job = await fetch('http://127.0.0.1:1108/render', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({
    levelPath: 'C:/Levels/MyLevel.adofai',
    preset: 'FullHD',
    bitrateMbps: 30,
    captureAudio: true,
    bgaMode: true
  })
}).then(response => response.json());

console.log(job);
```

## 성능

GPU readback과 FFmpeg 인코딩을 파이프라인으로 겹치며, raw 프레임을 임시 디스크 파일로 저장하지 않습니다. 버퍼가 부족하면 가장 오래된 readback 하나만 완료시켜 인코더에 전달하고, 나머지 요청은 계속 진행합니다. 렌더 종료 시에는 모든 요청을 완료합니다. 완료 로그에는 전체 렌더 시간(`totalWall`), 게임 프레임, readback 대기/복사, encoder backpressure/write, 오디오 캡처, 최종 mux 시간이 분리되어 기록됩니다. 지원되는 장치에서는 최종 RGB 결과를 유지하면서 raw 입력을 `rgb24`로 전송해 RGBA보다 프레임당 전송량을 25% 줄이고, 지원되지 않는 장치에서는 RGBA 경로를 사용합니다.

출력 FPS가 게임 시뮬레이션 FPS보다 높을 때 같은 게임 프레임의 추가 GPU readback을 생략합니다. 완료 로그의 `Performance detail`에는 준비 시간, 매 프레임 표시 상태 관리, 미리보기·진행창 UI 호출 시간, 인코더 버퍼 대기, 생략한 readback 수와 프로세스 메모리 피크가 기록됩니다. UI 시간은 CPU 호출 시간이며 GPU 실행 시간과는 다릅니다.

하드웨어 인코더를 사용할 때 FFmpeg의 입력 변환 필터 스레드를 2개로 제한해 게임과 동시에 사용할 CPU 여유를 확보합니다. 렌더 진행창은 표시 텍스트를 캐시하고 Repaint에서만 그리며, 취소 버튼은 입력 이벤트를 계속 처리합니다. 렌더 중에는 진행창에 불필요한 GUILayout 처리를 생략하고, 종료·취소·실패 후에는 복원합니다. 비트레이트, 인코더 프리셋, 출력 해상도와 FPS 설정은 유지됩니다.

일반 게임에서 200–500 FPS가 나오더라도 GPU readback, CPU 프레임 복사, 인코더 입력, 오디오 mux가 필요하므로 실제 렌더 완료 속도는 Video FPS와 다를 수 있습니다. Target FPS는 게임 시뮬레이션에, Video FPS는 최종 영상 스트림에 각각 적용되며, 비트레이트와 화질은 자동으로 낮추지 않습니다.

## 개발 및 테스트

OrbitRender는 `https://git.mizuki.guru/api/packages/imnyang/nuget/index.json`에서
정확히 `BlockMelonLoader 1.0.0` 버전의 NuGet 의존성을 복원하며,
빌드 시 라이브러리 소스 저장소는 필요하지 않습니다.
복원된 `BlockMelonLoader.dll`은 `OrbitRender.dll`과 함께 배포해야 합니다.

```powershell
.\build.ps1 -Test
```

macOS/Linux에서는 로컬 ADOFAI 관리 DLL과 MSBuild를 준비한 뒤 다음처럼 빌드합니다.

```bash
GAME_DIR="$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice" bash ./build.sh
```

macOS/Linux에서 FFmpeg 통합 테스트까지 실행하려면 Mono와 FFmpeg를 설치한 뒤 `TEST=1`을 추가합니다. `TMPDIR`이 사라진 `/var/folders/...`를 가리켜도 테스트는 `/tmp`로 자동 대체합니다.

```bash
GAME_DIR="$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice" TEST=1 FFMPEG_PATH=ffmpeg bash ./build.sh
```

다른 게임 경로는 `-GameDir` 또는 `GAME_DIR`, 특정 MSBuild는 `-MSBuildPath` 또는 `MSBUILD_PATH`로 지정합니다. 테스트는 프레임 수·순서, 인코딩 결과 일치, 실패·취소, AAC mux, A/V 길이 drift를 확인합니다.

## License

OrbitRender is licensed under the GNU General Public License v3.0
(`GPL-3.0-only`) with an additional linking exception for
A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
