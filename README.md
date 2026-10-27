# ❕❕중요❕❕

이 프로젝트는 대부분이 ai를 이용하여 제작되었으며 실제 환경에서의 동작을 보증하지 않습니다. 

# HP 시스템 관리 모드 선택기

HP OmniBook Ultra Flip 14 의 "시스템 관리(성능 제어)" 모드를 myHP 앱 없이 직접 선택하는 프로그램입니다.
myHP 의 `HP.SystemControl` 이 사용하는 것과 같은 BIOS WMI 호출(`root\wmi:hpqBIntM`, CommandType `0x4C`)을 사용합니다.

## 실행
`HpThermalMode.exe`를 실행하면 창 없이 작업표시줄 알림 영역(트레이)에 상주합니다. 관리자 권한이 필요하며 실행 시 UAC 창이 뜹니다.

- **왼쪽 한 번 클릭 / 오른쪽 클릭**: 모드 선택 메뉴를 열고, 원하는 모드를 누르면 바로 적용합니다. 현재 BIOS 모드는 체크 표시됩니다.
- **왼쪽 더블클릭**: 기존 GUI 창을 엽니다.
- **GUI의 X 버튼**: 프로그램을 종료하지 않고 트레이로 숨깁니다.
- **트레이 메뉴 → 종료**: 프로그램을 완전히 종료합니다.
- 트레이 아이콘이 안 보이면 작업표시줄의 숨겨진 아이콘(`^`) 영역을 확인하세요.

GUI로 바로 시작하려면 `HpThermalMode.exe --gui`를 사용하세요. 트레이 실행 자체는 모드를 변경하지 않습니다.
로그온 시 자동 실행하려면 작업 스케줄러에서 **로그온할 때 / 가장 높은 수준의 권한으로 실행 / 사용자가 로그온할 때만 실행**으로 이 실행 파일을 등록하세요. 자동 시작 작업은 프로그램이 직접 등록하지 않습니다.

## 모드 값
| BIOS 값 | 모드 |
|---|---|
| 5 | Smart Sense |
| 1 | 균형 |
| 2 | 저온 |
| 6 | 저소음 (이 노트북은 Quiet(3) 대신 이 값을 사용) |
| 4 | 전원 절약 |
| 0 | 성능 |

지원 모드는 BIOS(CommandType `0x0D`, 7번째 바이트)에서 읽어 표시합니다.
Smart Sense 일 때는 BIOS 가 상황에 따라 선택한 실제 모드를 보고하므로 "현재 상태"가 바뀔 수 있습니다.

## 명령줄 (작업 스케줄러 등에서 사용)
```
HpThermalMode.exe --get
HpThermalMode.exe --list
HpThermalMode.exe --tray                (트레이로 시작, 인수 없이 실행해도 동일)
HpThermalMode.exe --gui                 (GUI 창을 열고 시작)
HpThermalMode.exe --set silent          (smartsense, balanced, cool, silent, powersaver, performance)
HpThermalMode.exe --set performance --power   (Windows 전원 모드도 같이 변경)
HpThermalMode.exe --killhp              (HP.SystemControl.Background 종료)
```

## 주의: HP 백그라운드 프로세스
myHP 가 실행하는 `HP.SystemControl.Background.exe` 는 Windows 전원 모드가 바뀌는 등의 이벤트가 생기면
자신이 저장해 둔 모드(기본값 Smart Sense)로 BIOS 값을 되돌립니다.
- 그래서 "Windows 전원 모드도 함께 변경" 옵션은 기본적으로 꺼져 있습니다.
- 되돌려지는 경우 프로그램의 **HP 프로세스 종료** 버튼(또는 `--killhp`)을 사용하세요. myHP 앱을 다시 열면 다시 실행될 수 있습니다.
- 재부팅하면 BIOS 가 설정을 유지하는지는 확인하지 못했습니다. 유지되지 않으면 `--set` 을 로그온 시 작업 스케줄러(최고 권한)로 등록하세요.

## 빌드
`build.cmd` (Windows 기본 포함된 .NET Framework 의 `csc.exe` 사용, 별도 SDK 불필요)
