<h1 align="center">
  <img src="https://img.shields.io/badge/PLC%20%26%20C%23%20WPF%20Elevator%20Control%20System-007ACC?style=for-the-badge&logo=c-sharp&logoColor=white&labelColor=1F2328" alt="프로젝트명 배너" width="100%" height="50%">
</h1>

> **LS Electric XG5000 PLC & C# WPF Interlocking Project**

---

## 👥 프로젝트 팀원
* **윤은식**
* **최준영**

---

## 🚀 프로젝트 개요
본 프로젝트는 **Industrial Automation** 기술을 기반으로, PLC(XGK 시리즈)와 PC(C# WPF) 간의 **Ethernet 통신(MLDP 프로토콜)**을 통해 엘리베이터의 수직 이동 및 도어 개폐 시스템을 제어하고 실시간으로 상태를 모니터링하는 프로젝트입니다.

### 주요 기능
- **실시간 데이터 동기화**: PLC의 메모리(%M, %W)를 0.3초 주기로 스캔하여 WPF UI에 실시간 반영
- **안전 로직(Interlock)**: 문이 열려 있는 동안 엘리베이터 이동 금지 및 상충하는 모터 동작 차단 로직 구현
- **자동/수동 제어**: 시뮬레이터 버튼 입력과 C# UI 버튼 입력을 통한 하이브리드 제어 지원
- **로그 시스템**: PLC와의 모든 Read/Write 트랜잭션을 시간대별로 기록

---

## 🛠 기술 스택
* **PLC**: LS Electric XG5000 (XGK-CPUS 시뮬레이터)
* **PC**: C# .NET Framework (WPF)
* **Library**: XGCommLib.dll
* **Communication**: TCP/IP (Localhost 127.0.0.1 : 2004) - 시연용 TCP/IP (192.168.0.200 : 2004)

---

## 📊 시스템 아키텍처 및 변수 할당

### 1. 주요 디바이스 할당 (Memory Map)
`변수_명칭.xlsx`를 기반으로 정의된 핵심 주소입니다.

| 구분 | 주소 | 명칭 | 기능 설명 |
| :--- | :--- | :--- | :--- |
| **입력** | %IX0.0.0 | 문닫힘버튼 | 엘리베이터 문 닫힘 수동 입력 |
| **입력** | %IX0.0.1 | 문열림버튼 | 엘리베이터 문 열림 수동 입력 |
| **제어** | %MX40 | 문닫힘모터 | 문 닫힘 동작 수행 (자기유지 회로 포함) |
| **제어** | %MX41 | 문열림모터 | 문 열림 동작 수행 (인터록 적용) |
| **상태** | %MX30 | 문닫힘완료 | 문이 완전히 닫힌 상태 알림 |
| **상태** | %MX31 | 문열림완료 | 문이 완전히 열린 상태 알림 |
| **데이터** | %MW0 | 현재층수 | 엘리베이터의 현재 위치 정보 (Word) |

### 2. PLC 래더 로직 구조

- **시퀀스 제어**: 타이머(TON)를 사용하여 문 개폐 속도를 3초로 제어
- **데이터 처리**: `MOVE` 명령어를 사용하여 도착 완료 시 해당 층수 데이터를 `%MW0`에 저장

---

## 🖥 실행 화면

<div align="center">
  <video src="https://github.com/user-attachments/assets/5ec6924a-b659-48c0-8a59-d64adc1710a1" width="100%" controls autoplay muted loop>
    브라우저가 비디오 태그를 지원하지 않습니다.
  </video>


  
  <p><i>PLC 시뮬레이터와 C# WPF 연동 엘리베이터 제어 시연 영상 (40s)</i></p>
</div>

---

## ⚙️ 실행 방법
1. **XG5000**에서 래더 파일을 열고 `시뮬레이터 시작`을 클릭합니다.
2. Visual Studio에서 프로젝트를 열고 `XGCommLib.dll` 참조를 확인합니다.
3. 실행 후 IP에 `127.0.0.1`, Port에 `2004`를 입력하고 `Connect` 버튼을 누릅니다.
4. `2F UP` 또는 `1F DOWN` 버튼을 눌러 동작을 테스트합니다.

---


---

## ⬇️ 프로젝트 발표 자료

프로젝트 수행 결과에 대한 자세한 내용은 아래 프레젠테이션 파일에서 확인하실 수 있습니다.  
추후 업데이트

## ⬇️  PLC 레더 이미지 
[PLC 레더](https://docs.google.com/spreadsheets/d/16CeHcKHgthQDonH_-AnNWvGHKgS-vQ57DcOko0OXjik/edit?gid=1853242603#gid=1853242603) 

## ⬇️ PLC 레더 원본 파일 
[파일](https://github.com/user-attachments/files/24553181/ev_project_byte.zip) 

---


