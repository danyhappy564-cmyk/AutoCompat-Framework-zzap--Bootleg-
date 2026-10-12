# AutoCompat Framework — SPT 4.1 포팅 (zzap--Bootleg-)

### ⚠️ IMPORTANT NOTICE / DISCLAIMER

**Original Author:** Solid-Rhoads
**Original Repository:** AutoCompat-Framework
**Original Link:** https://github.com/Solid-Rhoads/AutoCompat-Framework
**License:** MIT (Copyright (c) 2025 Solid-Rhoads — see `LICENSE`)

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

## 변경 이력

- 2026-10-12 21:55 — SPT 4.1 서버(C#)용으로 처음 포팅. 원작은 3.11 TypeScript 모드라 4.1에서 못 돌아서, 같은 계산을 C#으로 다시 썼습니다. 4.1에서 바뀐 바닐라 아이템 29개가 "모드 아이템"으로 잘못 분류되지 않게 바닐라 ID 목록(`data/vanillaItems.json`)도 4.1.6 기준으로 새로 뽑았고, 4.1에서 새로 생긴 파편 탄(`shrapnel_vog30`)을 기본 블랙리스트에 추가했습니다.

## 이 모드가 하는 일

다른 모드(무기·부품·탄약)를 여러 개 쓰면 "모드 A 무기에 모드 B 스코프/탄이 안 꽂히는" 문제가 생깁니다.
이 모드는 서버가 켜질 때 한 번 모든 모드 아이템을 훑어서 **슬롯의 허용 목록에 자동으로 넣어 줍니다.**

- 같은 구경의 모드 탄을 모드 무기의 약실(Chambers)과 모드 탄창(Cartridges)에 추가
- 바닐라 부품이 들어가는 칸에 그 바닐라 부품을 복제해서 만든 모드 부품도 추가
- (옵션) 바닐라 부품의 "같이 못 다는 부품" 목록을 복제본에도 상속
- (옵션) `ManualAdd` 로 직접 지정한 조합 강제 추가

복제 원본은 ① 이름이 같은 바닐라 아이템 ② (`modFileParsing`) 다른 모드 폴더의 JSON 안 `itemTplToClone`/`clone` 순서로 찾습니다.

## 설치

1. 빌드 결과(`release/AutoCompatFramework-*.zip`)를 풀어서 `SPT_Runtime\user\mods\AutoCompatFramework` 폴더에 넣습니다.
2. 설정은 `config\config.jsonc` (주석으로 설명 있음).
3. 서버 콘솔에 `AutoCompatFramework Summary`가 나오면 동작한 것입니다. 안 맞는 조합이 있으면 `verboseLogging: true` 로 켜고 서버 로그를 확인하세요.

## 원작과 달라진 점 (4.1 포팅)

| 항목 | 원작 (3.11, TS) | 이 포팅 (4.1, C#) |
|---|---|---|
| 실행 시점 | `postDBLoad` | `PostLoad + 100000` — 다른 모드가 아이템/슬롯을 다 만든 뒤 (아이템을 새로 만들지는 않아서 DB 무결성 검사 무관) |
| 바닐라 ID 목록 | 3.11 기준 | **4.1.6 기준으로 재생성** (옛 목록이면 4.1 신규 바닐라 29개가 모드 아이템으로 오인됨) |
| `modFileParsing` | 복제 원본을 못 찾은 아이템마다 모든 모드 JSON을 다시 읽음 | **전체를 한 번만** 읽어 색인 (결과는 같고 훨씬 빠름) |
| 캐시 | 모드를 바꾸면 수동으로 폴더 삭제 | 설치된 모드 목록이 바뀌면 **자동으로 새로 만듦** (`cache/cache.json`) |
| `ManualAdd` | 표준 칸 이름이 대상에 없으면 "추가했다"고 세기만 하고 실제로는 안 넣는 경우가 있었음 | 실제 칸을 못 찾으면 복제 원본 기반 추론 → `mod_tactical` → 첫 칸 순으로 계속 찾고, 끝내 없으면 경고 |
| 로그 | winston | SPT 서버 로그 (`verboseLogging` 이 켜지면 상세 줄도 일반 로그로 출력) |

## 검증 상태 (정직하게)

- `dotnet build -c Release` 성공, 경고 0.
- **원작 TS 코드와 같은 데이터로 결과 비교**: 진짜 4.1.6 `items.json`(4,673개)에 합성 모드 아이템 225개 + 가짜 모드 JSON을 붙여 양쪽에 돌림. `modFileParsing` 켠/끈 설정에서 슬롯·충돌·구경 결과가 **일치**, `ManualAdd` 설정에서는 위 표의 의도된 차이만 있음.
- **실제 SPT 4.1.6 서버 기동 확인(컨테이너, 헤드리스)**: 모드 로드(GUID·버전 인식), 의존성 주입, 설정/데이터 파일 읽기, 다른 모드가 만든 복제 무기·부품·탄을 JSON에서 추적해 "탄 1개 약실 추가 / 부품 1개 슬롯 추가", 캐시 저장 후 재기동 시 캐시 적중까지 서버 로그로 확인.
- 인게임(게임 클라이언트 접속, 실제 모드 목록과의 호환 결과)은 **미검증** — 제가 게임을 못 돌립니다. 이상이 있으면 서버 콘솔 / `SPT_Runtime/user/logs` 를 확인해 주세요.

## 빌드

```
dotnet build -c Release
```

`E:\SPT 4.1\SPT_Runtime\user\mods` 가 있으면 빌드 후 자동 복사됩니다 (`-p:SptModsDir=경로` 로 변경).
