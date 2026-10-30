# OrbitRender

https://github.com/imnyang/OrbitRender/blob/main/README.en.md

OrbitRender는 A Dance of Fire and Ice(ADOFAI)의 커스텀 레벨을 영상으로 렌더링하는 Unity Mod Manager 모드입니다.

해상도, FPS, 비트레이트, 인코더 등을 직접 설정할 수 있으며, 게임을 실시간으로 녹화하지 않고 프레임 단위로 렌더링합니다.

## 주요 기능

- **영상 렌더링** — 최대 1024 Target FPS, 240 Video FPS 및 사용자 지정 해상도
- **다양한 인코더** — H.264, HEVC, VP9, AV1, ProRes 422 HQ, FFV1 (무손실) 및 NVIDIA NVENC, Intel QSV, AMD AMF, VAAPI, Vulkan 지원

ProRes는 FFmpeg `prores_ks` 소프트웨어 인코더로 10-bit 4:2:2 HQ 영상을 출력합니다. 컨테이너는 자동(MOV), MOV 또는 MKV를 선택하세요. 비트레이트와 인코딩 속도 설정 대신 HQ 프로파일을 사용합니다. 원본 캡처는 8-bit RGB이므로 10-bit 출력이 원본의 정밀도를 늘리지는 않습니다.

FFV1은 모든 픽셀이 그대로 보존되는 무손실 코덱입니다. 소프트웨어 인코딩으로 MKV에 기록되며 비트레이트와 인코딩 속도 설정이 적용되지 않습니다. FFmpeg 빌드와 GPU가 Vulkan FFV1 인코더(`ffv1_vulkan`)를 제공하면 사용 가능 여부 확인을 통과한 뒤 선택지에 나타나고, 자동 선택은 이를 우선합니다 — 이 경로는 RGB 표면을 그대로 업로드해 크로마 정보까지 완전히 보존합니다. 오디오도 FLAC(무손실)을 선택할 수 있습니다. 파일이 매우 크므로 편집·재인코딩용 마스터 영상으로 사용하는 것이 좋습니다.
- **오디오 캡처** — 게임 오디오를 함께 캡처하고 AAC 또는 Opus로 인코딩
- **BGA Mode** — 타일, 행성, 게임플레이 효과를 제외하고 배경만 렌더링
- **프리셋** — 기본 제공 프리셋과 사용자 지정 프리셋 저장
- **실시간 진행 상황** — 렌더 속도, 진행률, 예상 완료 시간 및 미리보기
- **RPC API** — 로컬 HTTP API를 통한 렌더링 자동화
- **자동 업데이트** — GitHub 정식 릴리즈를 확인하고 업데이트

Windows, macOS, Linux를 지원합니다.

## 설치

1. [Releases](../../releases)에서 최신 버전을 다운로드합니다.
2. 압축을 풀고 모드 폴더를 `A Dance of Fire and Ice/Mods/`에 넣습니다.
3. Unity Mod Manager가 설치된 상태에서 게임을 실행합니다.

렌더링에는 FFmpeg가 필요합니다. 설치되어 있지 않다면 첫 실행 시 자동 설치 여부를 묻습니다. 기존 FFmpeg를 사용하려면 설정에서 실행 파일 경로를 지정하면 됩니다.

FFmpeg는 모드 배포 파일에 포함되어 있지 않습니다.

## 사용법

ADOFAI 에디터에서 커스텀 레벨을 열고 `File → Export Video`를 선택하거나 `F6`을 누릅니다.

내보내기 화면에서 전체·선택 구간의 예상 영상 용량, 임시 파일을 포함한 필요 공간과 디스크 여유 공간을 확인할 수 있습니다. 재생 타이밍이 확정되면 공간을 다시 검사합니다. ProRes 용량은 보수적인 추정치이며 실제 크기를 보장하지 않습니다.

고급 설정의 FFmpeg 프레임 버퍼는 절약(64 MiB), 기본(256 MiB), 처리량 우선(512 MiB)을 지원합니다. CPU 프레임 풀 예산이며 GPU·Rust 네이티브 인코더 메모리는 별도입니다. 한 프레임이 예산보다 크면 최소 한 장을 사용합니다.

영상 인코딩 완료 후 오디오 결합에 실패하면 중간 영상, WAV와 `.recovery.json` 파일을 보존합니다. 오류 창의 **오디오 결합 다시 시도**를 누르거나, 재실행 후 내보내기 고급 설정에서 복구 파일을 선택하세요. 파일 묶음을 같은 폴더에 유지해야 하며, 복구 성공 시 중간 파일은 정리됩니다. 기존 최종 영상은 덮어쓰지 않습니다.



내보내기 입력값의 오류는 항목 아래에 즉시 표시되며, 실행 시 첫 오류가 있는 탭과 입력칸으로 이동합니다. FPS·비트 심도·인코더·오디오 설정에는 설명과 호환성 안내가 표시됩니다.

**목록에 추가** 또는 **작업 목록** 탭에서 현재 맵의 전체·선택 구간 렌더를 여러 설정으로 등록할 수 있습니다. 추가 시 설정과 출력 폴더를 고정하며, 순서 변경·삭제·순차 실행·현재 렌더 후 일시정지를 지원합니다. 실패·취소 시 남은 작업을 보존하고 멈춥니다. 다른 맵으로 바꾸면 자동 실행을 중단합니다. 작업 목록은 게임 실행 동안만 유지됩니다.

렌더 오류 창의 **오류 진단 정보 복사**는 실패 내용, 출력 설정, 인코더, 시스템·GPU 정보와 파일 경로를 클립보드에 복사합니다. 외부로 자동 전송하지 않습니다.

다른 모드에서 렌더링을 제어하려면 [C# API 문서](docs/CSharp-API.md)를 참고하세요.

## TUF 리플레이 렌더 (실험 기능)

TUFReplay를 활성화한 상태에서 기록에 사용한 원본 레벨을 에디터에서 엽니다. 내보내기 화면의 고급 설정에서 **현재 레벨의 TUF 리플레이 불러오기**를 누르고 원하는 기록을 선택하세요. 번들 준비가 끝나면 전체 렌더를 시작할 수 있습니다. 이미 내보낸 번들은 **TUF 리플레이 번들 선택**으로 직접 고를 수도 있습니다. 번들은 일반 리플레이 CSV 한 개가 아니라 `manifest.json`, `inputs.csv`, `hits.csv`가 함께 있는 폴더입니다. TUFReplay는 번들을 모드 폴더의 `RenderBundles/<jobId>/`에 생성합니다. 레벨 경로와 파일 해시가 기록과 일치해야 합니다.

이 기능은 실험 단계이며 실제 게임 내 검증이 필요합니다. 리플레이 렌더에서는 설치된 키 뷰어에 기록된 입력을 전달하고 모드 오버레이를 캡처합니다. 키 뷰어별 게임 내 호환성은 아직 검증되지 않았습니다. 녹화된 카메라·마이크 합성은 지원하지 않습니다. 작업 목록과 선택 구간 렌더도 지원하지 않습니다.
## 빌드

`./build.ps1` 또는 `GAME_DIR="게임 설치 경로" bash ./build.sh`로 빌드합니다. Rider에서 빌드해도 저장소의 `Build/` 폴더에 `OrbitRender-yyyyMMdd-HHmmss-fff.zip`이 자동 생성됩니다. ZIP 최상위에 모드 DLL, `Info.json`, 라이선스와 `OrbitRender/Native/` 아래의 Native DLL이 포함됩니다. Native DLL은 빌드 출력에서도 모드 루트에 배치됩니다. 번역 파일은 모드 DLL에 내장되므로 ZIP에 별도로 넣지 않습니다.

Windows에서는 저장소 옆의 `../OrbitRender.rs`가 있으면 Cargo로 Native DLL도 Release 빌드하여 ZIP과 모드 출력 루트에 포함합니다. Rust 툴체인과 MSVC 빌드 도구가 필요합니다. 다른 위치는 MSBuild의 `/p:NativeProjectDirectory="경로"`로 지정할 수 있습니다.

## License

OrbitRender is licensed under the GNU General Public License v3.0
(`GPL-3.0-only`) with an additional linking exception for
A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
