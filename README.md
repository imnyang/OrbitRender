# OrbitRender

https://github.com/imnyang/OrbitRender/blob/main/README.en.md

OrbitRender는 A Dance of Fire and Ice(ADOFAI)의 커스텀 레벨을 영상으로 렌더링하는 Unity Mod Manager 모드입니다.

해상도, FPS, 비트레이트, 인코더 등을 직접 설정할 수 있으며, 게임을 실시간으로 녹화하지 않고 프레임 단위로 렌더링합니다.

## 주요 기능

- **영상 렌더링** — 최대 1024 Target FPS, 240 Video FPS 및 사용자 지정 해상도
- **다양한 인코더** — H.264, HEVC, VP9, AV1 및 NVIDIA NVENC, Intel QSV, AMD AMF, VAAPI 지원
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

## 빌드

`./build.ps1` 또는 `GAME_DIR="게임 설치 경로" bash ./build.sh`로 빌드합니다. Rider에서 빌드해도 저장소의 `Build/` 폴더에 `OrbitRender-yyyyMMdd-HHmmss-fff.zip`이 자동 생성됩니다. ZIP 최상위에 모드 DLL, `Info.json`, 라이선스와 `OrbitRender/Native/` 아래의 Native DLL이 포함됩니다. Native DLL은 빌드 출력에서도 모드 루트에 배치됩니다. 번역 파일은 모드 DLL에 내장되므로 ZIP에 별도로 넣지 않습니다.

Windows에서는 저장소 옆의 `../OrbitRender.rs`가 있으면 Cargo로 Native DLL도 Release 빌드하여 ZIP과 모드 출력 루트에 포함합니다. Rust 툴체인과 MSVC 빌드 도구가 필요합니다. 다른 위치는 MSBuild의 `/p:NativeProjectDirectory="경로"`로 지정할 수 있습니다.

## License

OrbitRender is licensed under the GNU General Public License v3.0
(`GPL-3.0-only`) with an additional linking exception for
A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
