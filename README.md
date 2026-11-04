# EastAsiaMap v10.1 Windows load compatibility candidate

[Download EastAsiaMap_v10_1.zip](EastAsiaMap_v10_1.zip?raw=true)

게임과 런처를 종료하고 ZIP을 새 폴더에 전부 푼 다음 EastAsiaMap_Install.exe를 실행합니다. 게임 위치를 자동으로 찾지 못하면 배너로드 설치 폴더를 선택합니다.

자체 DLL을 .NET Framework4.7.2/x64로 재빌드하고 netstandard의존성을 없앴습니다. 설치기는 검증된 자체 DLL만 파일 차단을 해제하고, 실제 게임 DLL을 사용한 로드 검사를 먼저 수행합니다. 통과하면 기존 모드를 게임 설치 위치의 EastAsiaMap_Backups에 보관하고 교체합니다. Native/NavalDLC 파일은 변경하지 않습니다.

Windows UI/NTFS/실제게임CLR로드와 캠페인 실행은 이 클라우드에서 검증하지 못했습니다. 화면의 일반적인Cannotload문구만으로 이전 로드실패 원인을 확정할 수는 없었습니다. 설치기파일작업9건, 캐시관리동작10건, 최종패키징6건 검사는 통과했습니다. 실제로드 실패 시 압축을 푼 폴더의 EastAsiaMap_install-check.log에 예외/HRESULT를 남깁니다.
