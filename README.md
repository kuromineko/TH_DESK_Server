<div align="center">

![Uploading fff44b85-48dd-44df-af25-d9f0b7237f5a.png…]()


# 🖥️ TH_DESK Server Manager (x64)
**โปรแกรมจัดการเซิร์ฟเวอร์รีโมทเดสก์ท็อปสำเร็จรูป รองรับ DDNS, แยกพอร์ต IN/OUT และเปิด Firewall อัตโนมัติ**  
*All-in-One Self-Hosted Server Manager for TH_DESK / RustDesk (Windows 64-bit)*

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0071FF?style=for-the-badge&logo=windows)](https://github.com/kuromineko/TH_DESK_Server/releases)
[![Framework](https://img.shields.io/badge/.NET%2010-WPF%20Dark%20UI-512BD4?style=for-the-badge&logo=dotnet)](https://github.com/kuromineko/TH_DESK_Server)
[![License](https://img.shields.io/badge/License-AGPL--3.0-10B981?style=for-the-badge)](https://www.gnu.org/licenses/agpl-3.0.html)
[![Client Repo](https://img.shields.io/badge/Client%20Repo-TH__DESK-38BDF8?style=for-the-badge&logo=github)](https://github.com/kuromineko/TH_DESK)

**[🇹🇭 อ่านภาษาไทย](#-คุณสมบัติเด่น-key-features) | [🇬🇧 Read in English](#-english-documentation)**

</div>

---

## ✨ คุณสมบัติเด่น (Key Features)

* 📦 **All-in-One Embedded Binaries:** ฝังคอร์เซิร์ฟเวอร์ `hbbs.exe` (ID Server) และ `hbbr.exe` (Relay Server) รุ่น 64-bit ไว้ในตัวโปรแกรม เปิดใช้งานได้ทันทีในไฟล์เดียวโดยไม่ต้องตั้งค่าผ่าน Command Line
* 🌐 **DDNS & Custom Port Forwarding (แยก IN / Out Port):** ออกแบบมาเพื่อการใช้งานร่วมกับเร้าเตอร์และ DDNS โดยเฉพาะ สามารถแยกตั้งค่า **IN Port (พอร์ตภายในเครื่องคอม)** และ **Out Port (พอร์ตภายนอกบนเร้าเตอร์/DDNS)** ได้อย่างอิสระ
* 🛡️ **Auto Windows Firewall:** เปิดพอร์ต TCP และ UDP ใน Windows Firewall ให้ตรงตามหมายเลขพอร์ตที่ตั้งไว้อัตโนมัติทันทีที่กดเริ่มระบบ
* ⚡ **Built-in Port Checker:** มีปุ่มทดสอบสถานะพอร์ตในตัว เช็กได้ทันทีว่าพอร์ตในเครื่องทำงานปกติและพอร์ตภายนอกทะลุเร้าเตอร์ (Port Forwarding) สำเร็จหรือไม่
* 📥 **Zero-Config Client Export:** ส่งออกไฟล์ตั้งค่าเครื่องลูกข่าย (`TH_DESK2.toml` และสคริปต์ติดตั้งคลิกเดียว `Setup_TH_DESK_Client.bat`) ให้ผู้ใช้งานปลายทางดับเบิลคลิกเชื่อมต่อเข้าเซิร์ฟเวอร์ได้ทันทีโดยไม่ต้องกรอก IP หรือ Public Key เอง
* 🚀 **Auto-Start on Windows Boot & System Tray:** รองรับการตั้งค่าเปิดระบบเซิร์ฟเวอร์อัตโนมัติเมื่อเปิดเครื่อง Windows (ผ่าน Task Scheduler สิทธิ์สูงสุด) พร้อมพับเก็บลงมุมขวาล่างข้างนาฬิกา (System Tray)
* 🇹🇭 **Bilingual Modern Dark UI (TH / EN):** หน้าตาโปรแกรมโทนมืด (Dark Mode) สไตล์เดียวกับตัวรีโมท รองรับการสลับภาษา **ไทย / English** แบบเรียลไทม์

---

## 🔌 การตั้งค่าพอร์ตและเร้าเตอร์ (Port Forwarding Guide)

| บริการ (Service) | โปรโตคอล (Protocol) | IN Port (พอร์ตในเครื่อง) | Out Port (พอร์ตนอก DDNS) | หน้าที่การทำงาน |
| :--- | :---: | :---: | :---: | :--- |
| **ID Server (`hbbs`)** | **TCP + UDP** | เช่น `1555` *(และพอร์ตข้างเคียง ±2)* | เช่น `4997` | ระบบจับคู่เครื่อง รอรับการเชื่อมต่อและตรวจสอบ Key |
| **Relay Server (`hbbr`)** | **TCP Only** | เช่น `1556` | เช่น `4998` | ระบบส่งภาพสำรองกรณีเครือข่ายปลายทางไม่สามารถเชื่อมต่อแบบ P2P ตรงได้ |

## 🚀 วิธีใช้งาน (Quick Start - TH)

1. ดาวน์โหลดไฟล์ **`TH_DESK_Server.exe`** จากหน้า [Releases](https://github.com/kuromineko/TH_DESK_Server/releases)
2. เปิดโปรแกรม (สิทธิ์ Administrator) แล้วกรอกชื่อ **โดเมน DDNS หรือ Public IP** ของคุณ
3. ตั้งหมายเลข **IN Port (ในเครื่อง)** และ **Out Port (พอร์ตนอก)** ตามที่ตั้งค่าไว้ในเร้าเตอร์
4. กดปุ่ม **"▶ เปิดระบบ Server + เปิด Firewall"** โปรแกรมจะคลายไฟล์เซิร์ฟเวอร์ เปิดพอร์ต Firewall และสร้างรหัส **Public Key** ให้อัตโนมัติ
5. กดปุ่ม **"📥 ส่งออกไฟล์ลูกข่าย"** เพื่อนำไฟล์ `Setup_TH_DESK_Client.bat` ไปรันที่เครื่องลูกข่าย (ตั้งค่าเชื่อมต่ออัตโนมัติโดยไม่ต้องกรอก IP/Key เอง)

---

# 🇬🇧 English Documentation

## ✨ Key Features

* 📦 **All-in-One Embedded Binaries:** Bundles official 64-bit `hbbs.exe` (ID Server) and `hbbr.exe` (Relay Server) binaries directly inside the executable. Extracts and runs automatically with a single click—no CLI setup required.
* 🌐 **DDNS & Asymmetric Port Forwarding (IN / Out Port):** Built specifically for custom router NAT/Port Forwarding setups. Easily map local machine listening ports (**IN Port**) to external router/DDNS ports (**Out Port**).
* 🛡️ **Automated Windows Firewall Rules:** Automatically configures inbound TCP and UDP rules in Windows Defender Firewall for your selected local ports upon server startup.
* ⚡ **Built-in Port Reachability Tester:** Instantly verifies both local listening ports (`127.0.0.1`) and external DDNS/Public IP port forwarding with a single click.
* 📥 **Zero-Config Client Exporter:** Generates ready-to-use client configuration files (`TH_DESK2.toml` and a one-click `Setup_TH_DESK_Client.bat` installer) so end-users can connect without manually typing IP addresses, ports, or public keys.
* 🚀 **Auto-Start on Boot & System Tray:** Registers a highest-privilege Windows Task Scheduler job to launch the server automatically on Windows boot and minimize cleanly to the System Tray.
* 🌐 **Bilingual Dark UI (TH / EN):** Modern WPF Dark Theme matching the client interface with instant Thai / English language switching.

---

## 🔌 Port Configuration Overview

| Service | Protocol | IN Port (Local PC) | Out Port (External DDNS) | Description |
| :--- | :---: | :---: | :---: | :--- |
| **ID Server (`hbbs`)** | **TCP + UDP** | e.g., `1555` *(±2 adjacent ports)* | e.g., `4997` | Rendezvous, ID registration, NAT punch, and key verification |
| **Relay Server (`hbbr`)** | **TCP Only** | e.g., `1556` | e.g., `4998` | Fallback relay traffic when direct P2P connection is unavailable |

## 🚀 Quick Start (EN)

1. Download **`TH_DESK_Server.exe`** from the [Releases](https://github.com/kuromineko/TH_DESK_Server/releases) page.
2. Run the application as **Administrator** and enter your **DDNS Domain or Public IP**.
3. Configure your **IN Port (Local PC)** and **Out Port (External Router/DDNS)** for both `hbbs` and `hbbr`.
4. Click **"▶ Start Server & Open Firewall"**. The manager will extract the server binaries, configure Windows Firewall, and generate your `id_ed25519.pub` **Public Key**.
5. Click **"📥 Export Client File"** to generate `Setup_TH_DESK_Client.bat` for zero-configuration deployment on client machines.

## 🛠️ Build from Source

* **Requirements:** Windows 10/11 (x64), Visual Studio 2022/2026, .NET 10.0 SDK (`net10.0-windows`)
* Open `TH_DESK_Server.sln` in Visual Studio, set configuration to `Release | x64`, and build or publish as a Single-File executable.

---

## 📜 เครดิตและสัญญาอนุญาต (Credits & License)

* **Developer & Maintainer:** [kuromineko](https://github.com/kuromineko)
* **Client Repository:** [kuromineko/TH_DESK](https://github.com/kuromineko/TH_DESK)
* **Upstream Projects:** Built upon and powered by [RustDesk](https://github.com/rustdesk/rustdesk) and [RustDesk Server OSS (`hbbs` / `hbbr`)](https://github.com/rustdesk/rustdesk-server).
* **License:** Distributed free of charge with full source code availability under the **[GNU Affero General Public License v3.0 (AGPL-3.0)](https://www.gnu.org/licenses/agpl-3.0.html)**.
