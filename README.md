# 🛗 PLC & WPF Elevator Control System

> **XG5000 PLC 시뮬레이터와 C# WPF를 연동한 실시간 엘리베이터 모니터링 및 제어 프로젝트**

---

## 👥 팀 구성 (Team)
* **최준영**: PLC 래더 로직 설계 및 C# 통신 인터페이스 구현
* **윤은식**: PLC 래더 로직 설계 및 UI/UX 디자인

---

## 🚀 프로젝트 개요 (Project Overview)
본 프로젝트는 산업 현장에서 널리 쓰이는 **LS Electric PLC**와 PC 간의 통신 기술을 활용하여 엘리베이터 시스템을 가상으로 제어하고 모니터링하는 것을 목표로 합니다. **XGCommLib** 라이브러리를 통해 PLC 메모리에 실시간으로 접근하여 데이터를 처리합니다.

### 🔑 주요 기능
1. **실시간 데이터 바인딩**: PLC의 `%MW0`(층수) 데이터를 읽어 UI에 즉각 반영
2. **도어 제어 시퀀스**: 3초 타이머와 인터록 회로를 통한 실제와 유사한 문 개폐 동작
3. **상태 모니터링**: 문 열림/닫힘 상태(`%MX30`, `%MX31`)를 시각적으로 표현
4. **통신 로그**: PLC와의 모든 트랜잭션을 실시간으로 로깅

---

## 🛠 기술 스택 (Tech Stack)
* **Development Environment**: Visual Studio 2022
* **Language/Framework**: C#, WPF (.NET Framework)
* **PLC Tool**: XG5000 Simulator
* **Library**: XGCommLib (MLDP Protocol)
* **Protocol**: TCP/IP (Local Loopback 127.0.0.1:2004)

---

## 📑 시스템 구조

### PLC 메모리 맵 (Memory Map)
| 구분 | 주소 | 역할 |
| :--- | :--- | :--- |
| **Input** | %IX0.0.0 / 0.0.1 | 물리 버튼 입력 (문닫힘 / 문열림) |
| **Internal** | %MX1 / %MX2 | 버튼 입력 내부 비트 |
| **Control** | %MX40 / %MX41 | 문닫힘 / 문열림 모터 구동 비트 |
| **Status** | %MX30 / %MX31 | 문닫힘 / 문열림 완료 상태 비트 |
| **Data** | %MW0 | 현재 층수 데이터 저장 (Word) |

### 래더 로직 주요 구조

* **자기유지**: 버튼을 떼어도 동작이 유지되도록 설계
* **인터록**: 열림/닫힘 동작이 동시에 일어나지 않도록 보호
* **데이터 이동**: 도착 완료 시 `MOV` 명령어로 층수 데이터 업데이트

---

## 🖥 실행 화면
> **Tip**: 아래 이미지 경로에 실제 캡처한 사진을 넣어보세요!

| MainWindow (Log/Conn) | Elevator Monitor (UI) |
| :---: | :---: |
| ![Main](https://via.placeholder.com/400x250?text=MainWindow+Log) | ![UI](https://via.placeholder.com/250x400?text=Elevator+UI) |

---

## ⚙️ 설치 및 실행 방법
1. **XG5000**에서 래더 파일을 열고 **시뮬레이터**를 시작합니다.
2. 시뮬레이터가 `RUN` 모드인지 확인 후 프로그램을 '쓰기' 합니다.
3. C# 프로젝트를 빌드 및 실행합니다.
4. IP `127.0.0.1`, Port `2004`를 입력하고 **Connect** 버튼을 누릅니다.

---
