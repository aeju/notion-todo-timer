# notion-todo-timer
Always-on-top WPF window for Notion to-dos — check off today's and backlog tasks, and run a countdown timer per task.

개인용으로 만든 Notion 할 일 + 타이머 창.

만든 이유

할 일은 Notion에, 타이머는 따로 있어서 할 일 하나에 시간 걸 때마다 왔다 갔다 해야 했다. 브라우저에 띄운 목록은 다른 창 뒤로 숨는다. 그래서 둘을 작은 창 하나로 합쳤다.

기능
오늘: '할 일' DB에서 날짜 = 오늘인 것. 끝낸 건 취소선으로 남고 헤더에 완료/전체
장기: '할 일 (장기)' DB의 미완료 (접어둘 수 있음)
체크: ○/● → Notion 완료 체크박스에 반영. 오늘은 체크/해제, 장기는 체크하면 빠짐
추가: 입력하고 Enter → '할 일' DB에 오늘 날짜로 들어감
타이머: ▶ → 그 할 일로 카운트다운 (기본 25분). 60분 원판에 빨간 부채꼴로 남은 시간
끝났을 때: 알림음 3번 + 창이 앞으로 옴 + [끝 / +5분 / 중단]. 끝 누르면 Notion에 완료
📌: 다른 창 위 고정 켜고 끄기 (기본 켜짐). 미니 모드는 타이머만
날짜 기준: 새벽 5시 전은 전날. 켜둔 채 날 바뀌면 알아서 다시 불러옴
구조
FocusBar/
├─ Services/NotionClient.cs     Notion API 호출 (조회·추가·완료)
├─ ViewModels/MainViewModel.cs  목록 상태, 타이머 상태(Idle/Running/Finished)
├─ Models/TodoItem.cs           할 일 1건 (오늘/장기, 완료 여부)
├─ Controls/TimerDial.cs        타임타이머 원판 (OnRender로 그림)
├─ AppSettings.cs               설정 로드, DB 링크에서 ID 추출
└─ MainWindow.xaml              화면

.NET 8 WPF, NuGet 패키지 없음. Notion API 2022-06-28.

메모
웹 말고 WPF인 이유: 브라우저에선 CORS 때문에 Notion API 직접 호출이 안 됨. 위 고정도 Topmost 하나면 끝
오늘/장기 DB를 나눈 이유: 매일 생기는 할 일이랑 한 번 하고 끝나는 일을 섞으면 미완료가 계속 쌓임. 오늘 DB는 날짜로 필터해서 그날 것만 가져옴
남은 시간은 종료 시각에서 거꾸로 계산 (틱마다 1초씩 빼면 오차 쌓임)
Notion은 한 번에 100건까지라 next_cursor로 끝까지 이어서 가져옴
세팅
Notion 통합에서 연결 만들기 (액세스 토큰). 콘텐츠 읽기·업데이트·삽입, 사용자 정보 없음
DB 두 개 있는 페이지 → ··· → 연결 → 추가
appsettings.example.json → appsettings.json 복사해서 채우기
json
   {
     "NotionToken": "ntn_...",
     "TodayDatabaseId": "'할 일' DB 링크",
     "LongTermDatabaseId": "'할 일 (장기)' DB 링크",

     "TitleProperty": "할 일",
     "DueProperty": "마감",
     "DoneProperty": "완료",
     "DonePropertyType": "checkbox",
     "DateProperty": "날짜",
     "DayStartHour": 5,

     "DefaultMinutes": 25,
     "ExtendMinutes": 5,
     "DialScaleMinutes": 60
   }

DB 링크는 통째로 붙여도 ID만 뽑아 씀

Visual Studio에서 FocusBar.csproj 열고 실행

appsettings.json은 토큰 들어가서 .gitignore에 있음.

필요한 Notion 속성
할 일 (오늘): 할 일(제목), 완료(체크박스), 날짜(날짜), 마감(날짜, 선택)
할 일 (장기): 할 일(제목), 완료(체크박스), 마감(날짜, 선택)