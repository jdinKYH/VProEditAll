# VProEditAll WORKLOG

VisionPro vpp 편집기 — QuickBuild(CogJobManager) / Job(CogJob) / ToolGroup / ToolBlock 를 한 프로그램에서 불러와 편집·저장.
VProEditorGroup1 을 바탕으로 새 프로젝트로 작성. 장비 PC 에서도 사용하는 것을 전제로 함.

## 2026-11-05 (261105@Claude) 1~5단계 구현

### 결정 사항 (사용자 결정 + 권장안 적용)
| # | 항목 | 적용 |
|---|---|---|
| 1 | 형식 선택 | 자동 판정(파일 바이트 검사) + 형식 표시 |
| 2 | QuickBuild 편집 | Job 선택 콤보 → 해당 Job 의 ToolGroup 편집, 저장은 JobManager 전체 |
| 3 | 저장 옵션 | 사용자 선택, 기본 All (Minimum 선택 가능, ExcludeDataBindings 는 제외) |
| 4 | 덮어쓰기 보호 | 임시 파일(.saving.tmp)에 저장 → 원본 교체, 기존 원본은 .bak 보관 |
| 5 | 사용 PC | 장비 PC 포함 (사용자 결정) |
| 6 | 카메라·I/O | 불러오기 전 경고 (GigE / AcqFifo / I/O 설정 포함 시) |
| 7 | 편집기 안 실행 | 금지: SuspendElectricRuns + 툴바 실행 버튼 숨김 |
| 8 | 진행 범위 | 전체 (사용자 결정) |
| 9 | 변경 감지 | 파일이 열려 있으면 다른 파일 로드/종료 시 항상 저장 여부 확인 |

### 파일 구성
- `VppInspector.cs` : 바이트만 읽어 형식/카메라/I/O 판정 (VisionPro 로드 없음). ToolBlock 을 ToolGroup 보다 먼저 판정.
- `VppDocument.cs`  : 불러오기 / 편집 대상 / 저장(임시→교체→.bak) / 정리(Stop → Shutdown 또는 Dispose)
- `MainForm.cs`     : UI (1줄: 로드·파일명·다른 이름으로 저장·저장 / 2줄: 형식·Job·저장 옵션 / 하단 상태줄)
- 플랫폼 x64 단일 구성 (MSB3270 경고 제거)

### 확인 결과
- 빌드: 오류·경고 없음
- 바이트 판정: VINA 폴더 vpp 27개 모두 예상 형식으로 판정 (AcqFifo 단독 파일은 미지원 처리)
- ToolGroup3.vpp 사본 왕복 시험 (카메라 설정 없음): 불러오기 / 새 파일 저장 / 덮어쓰기(.bak 생성) / 실패 시 원본 유지 / 다시 불러오기 툴 구성 동일 — 모두 통과
- Minimum 저장: 15.2MB → 5.1MB. PMAlign 학습 상태(Trained, TrainImage) 유지, InputImage 만 빠짐
- 프로그램 실행(파일 없이) 화면 확인, 정상 종료

## 2026-11-05 (261105@Claude) 카메라 파일 시험 (QuickBuild 종료 후, 사본으로 진행)

시험 방법: 원본은 scratchpad 로 복사해서 사용. 실제 MainForm 코드(편집 화면 연결/버튼 숨김/정리/종료)를 시험 프로그램에서 구동.

| 시험 | 결과 |
|---|---|
| ToolBlock (toolBlock01) | 통과. CogToolBlockEditV2 로 연결, Inputs/Outputs 트리·입력/출력 탭 표시. 저장→다시 불러오기 동일. 불러오는 데 7~13초 |
| Job (BlackJob1) | 통과. Job 1개 표시, ToolGroup(툴 5개) 편집, 저장→다시 불러오기 동일 |
| QuickBuild (1026_EditorGroup) | 통과. 저장→다시 불러오기 동일 |
| QuickBuild Job 2개 (시험용으로 만든 파일) | 통과. Job 콤보 전환 시 편집 화면 다시 연결, 버튼 숨김 유지 |
| 툴바 버튼 숨김 | 실제 툴팁 확인 후 보완: 실행 / **루프** / **열기 / 저장 / 다른 이름으로 저장** 숨김 |
| I/O | QuickBuild 18개 모두 IOEnable=False, Job 1개. 불러온 뒤 TCP 포트 열림 없음 (카메라 UDP 3956 만) |
| 종료 후 카메라 | 편집기 종료 후 다른 프로세스에서 영상 취득 정상 (Dahua A5201MG50E, 1920x1200) |

### 시험 중 수정
- 툴바 숨김 대상에 "루프", "열기", "저장", "다른 이름으로 저장" 추가.
  편집 화면 자체 저장 버튼은 상단 저장(백업)을 거치지 않고, QuickBuild/Job 에서는 ToolGroup 만 따로 저장되어 형식이 바뀌기 때문.
- 불러오기 경고/상태줄에 "카메라는 편집기를 종료해야 해제됨" 안내 추가.

### 확인된 제약
- **GigE 카메라는 편집기 프로세스가 종료되어야 해제됨.** 파일을 닫고 Shutdown + Disconnect(false/true) + GC + 20초 대기를 해도
  다른 프로세스에서 "요청한 하드웨어를 다른 프로세스에서 이미 사용하고 있습니다". 
  → 장비 PC 에서는 편집 후 편집기를 종료하고 생산 프로그램을 실행해야 함.
- IOEnable=True 인 QuickBuild 파일은 없어서, I/O 가 켜진 파일을 불러올 때의 동작은 시험하지 못함.

## 2026-11-05 (261105@Claude) 계획 대비 점검 및 보완

### 계획 대비 점검
| 단계 | 계획 항목 | 상태 |
|---|---|---|
| 1 | 판정 순서 / 자원 해제 순서 / 저장 실패 대비 / 대기 커서 | 완료 |
| 2 | 자동 판정 / 미지원 안내 / ToolBlock 형 Subject | 완료 |
| 2 | XML(SOAP) 대비 | 판정 코드 있음. 실제 시험: Cognex 객체는 SOAP 저장 자체가 실패 → XML vpp 는 현실적으로 없음. 판정 불가 시 불러온 객체로 다시 판정 |
| 2 | **스크립트 컴파일 오류 처리** | **누락 → 보완** (아래) |
| 3~5 | Job / QuickBuild / 저장 옵션 / 변경 확인 | 완료 |

### 보완 내용
1. **스크립트 컴파일 오류 확인** (`VppDocument.GetScriptErrors`)
   - VisionPro 는 스크립트 오류가 있어도 불러오기 예외를 내지 않고 `ScriptError` 에 남김.
   - `ScriptError` 는 정상일 때도 컴파일 로그("-Referenced Assembly", "-Found possible Script Class")를 담고 있음 → '-' 로 시작하지 않는 줄만 오류로 판단.
   - `CogScriptSupport.GetErrors()` 는 시험해 보니 오류가 있어도 빈 값 → 사용 안 함.
   - 불러온 뒤 오류가 있으면 경고창(최대 10건) + 상태줄 표시.
   - 시험: 원본 26개 오탐 0건, 오류 넣은 사본 정확히 검출 ("Locate : Compile Error in Script: ...").
   - 참고: 오류 스크립트가 있으면 VisionPro 자체가 불러오는 중에 "Unable to load script" 창을 먼저 띄움.
2. **편집 화면 연결 오류 처리**: BindEditor 예외 시 프로그램 종료 대신 메시지.
3. **전역 예외 처리** (Program.cs): UI 스레드 예외는 메시지 후 계속 실행.
4. **Minimum 저장 확인창**: 입력 이미지·결과가 빠진다는 확인 후 저장.

### 시험 중 확인된 사항
- 카메라 파일을 불러온 프로세스가 **카메라 Disconnect 없이 종료하면 R6025(pure virtual function call) 오류창**이 뜨고 멈춤.
  편집기는 종료 시 Disconnect 하므로 해당 없음 (GuiTest 종료 정상). → 종료 시 Disconnect 는 꼭 유지해야 함.
- 회귀 시험(ToolGroup3 저장/백업/다시 불러오기) 모두 통과.

### 남은 확인
- [x] 사용자 화면 조작 확인 (2026-11-05 사용자 확인 완료)
  - 초기 화면 / 카메라·I/O 경고창 / QuickBuild 로드(편집 화면 + 디스플레이) 정상
  - 저장(.vpp.bak 생성, Minimum 확인창) / 다른 이름으로 저장(파일명 변경) / 다른 파일 로드 시 저장 확인 / 종료 시 저장 확인 → 종료 후 QuickBuild 카메라 정상
- [ ] IOEnable=True 파일이 생기면 불러올 때 I/O 연결 여부 확인

## 2026-11-05 (261105@Claude) 사용자 요청 4건 반영 (권장안)

| # | 요청 | 반영 |
|---|---|---|
| 1 | 툴 편집기 실행은 원래대로 (테스트 필요) | "실행"/"루프" 다시 표시, SuspendElectricRuns 제거(원래 동작). "열기/저장/다른 이름으로 저장"은 계속 숨김. 결정 7번 변경 |
| 2 | 초기 화면에 빈 Job 편집 화면 + 빈 디스플레이 | 시작 시 Subject 없는 CogToolGroupEditV2 표시 (`ShowEmptyEditor`). 빈 상태에서도 열기/저장 숨김 |
| 3 | 파일이 열린 상태에서 로드 시 초기 화면으로 지웠다가 표시 | 파일 선택·경고 확인 후 `CloseCurrent()` → 빈 화면 → `Refresh()` → 불러오기 → 편집 화면. 취소하면 현재 파일 유지 |
| 4 | 버튼 입체감 | `Button3D.cs` 추가 (그라데이션 + 그림자, 마우스 올림/눌림/비활성 구분). 기존 FlatStyle 방식(ApplyPressEffect) 제거 |

시험 (ToolGroup3 사본, 카메라 없음): 초기 화면 / 로드 / 다시 로드 시 초기화 / 툴바(실행·루프 표시, 열기·저장 숨김) / 버튼 상태 그림 / 종료 — 모두 정상.

추가: "다른 이름으로 저장"/"저장"이 비활성(파일 로드 전)일 때 평평하게 보이던 문제 → 비활성도 그림자·그라데이션 유지, 글자만 회색 (Button3D).

※ 편집기 안에서 Image Source(AcqFifo) 를 실행하면 실제 카메라로 취득함 → 생산 프로그램을 먼저 종료해야 함 (불러오기 경고창에 안내 중).

## 2026-11-06 현재 상태
- 1~5단계 + 사용자 요청 4건(실행 원복, 초기 빈 화면, 로드 시 초기화, 3D 버튼) 완료, 사용자 화면 확인 완료.
- Git 저장소 생성 후 첫 커밋 (bin/obj/.vs 제외).
- 남은 확인: IOEnable=True QuickBuild 파일 시험 (파일 생기면).
