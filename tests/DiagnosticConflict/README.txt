OrbitRender 진단 로그 테스트 전용 모드 (0.0.1)

1. 최신 진단 개선 빌드의 OrbitRender를 설치합니다.
2. 이 폴더를 게임의 Mods/OrbitRender.DiagnosticConflict에 넣고 게임을 실행합니다.
3. UMM에서 OrbitRender Diagnostic Conflict TEST ONLY를 활성화하고 렌더링을 시작합니다.
4. 호환성 오류가 발생하면 오류 진단 정보 복사를 누릅니다.
5. 로그에서 아래 항목을 확인합니다.
   - OrbitRender의 실제 배포 버전
   - 모드 목록의 OrbitRender.DiagnosticConflict 0.0.1, active=True
   - Timing compatibility details의 Transpiler: owner=OrbitRender.DiagnosticConflict
   - 패치 함수 OrbitRenderDiagnosticConflict.Main.HideClock과 priority=800
   - 게임 버전, MVID, Original IL SHA256
6. 테스트 모드를 비활성화하거나 폴더를 제거한 뒤 게임을 완전히 재시작합니다.
7. 같은 렌더링을 다시 실행해 테스트 모드 없이도 발생하는 오류인지 비교합니다.

이 모드는 Time.unscaledTime 호출을 같은 값을 반환하는 래퍼로 대체합니다.
게임 파일을 수정하지 않으며, 원래 값은 유지하지만 OrbitRender의 직접 호출 탐지를 실패시킵니다.
원래 게임 메서드에 해당 호출이 없으면 UMM 로그에 wrapped 0 call(s)가 표시됩니다.
그 경우 기존 게임/모드 조합부터 이미 호환성 검사를 실패할 수 있으므로 대조 테스트가 필요합니다.
일반 렌더링 시에는 반드시 제거하세요. OrbitRender 정식 배포물에는 포함하지 않습니다.
