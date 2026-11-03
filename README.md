# EastAsiaMap v10 candidate

[Download EastAsiaMap_v10.zip](EastAsiaMap_v10.zip?raw=true)

Bannerlord1.5.6.125053용 수정 후보입니다. v9는 사용자 실행에서 캠페인 로딩 중 종료됐습니다. 실행 로그에서 동아시아 씬이 기본 NavalDLC 거리 캐시를 읽는 것을 확인하여 현재 지도용 캐시를 생성하는 런타임 DLL을 추가했습니다. 이 불일치가 직접 충돌 원인인지는 스택이 없어 확정되지 않았습니다.

정확한1.5.6 API 참조로 컴파일했고 관리형 동작10개와 패키징6개 검사를 통과했습니다. 실제 게임 엔진에서의 실행 성공은 아직 검증하지 못했습니다.

기존 EastAsiaMap_Trial 폴더는 Modules 밖에 보관하고 ZIP의 EastAsiaMap_Trial 폴더를 설치합니다. NavalDLC가 필요하며 새 샌드박스로 확인합니다. DLL이 새로 포함되므로 Windows ZIP 속성에 차단 해제가 보이면 적용한 뒤 압축을 풉니다. 첫 지도 캐시 생성 중 로딩이 더 걸릴 수 있습니다.

진단 파일은 Modules/EastAsiaMap_Trial/RuntimeDiagnostics에 기록됩니다. 세부 안내는 ZIP의 README_KO.txt에 있습니다. 사용자 로그 원문이나 개인 정보를 이 저장소에 업로드하지 않았습니다.
