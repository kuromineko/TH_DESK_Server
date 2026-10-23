<div align="center">

<img width="2170" height="725" alt="fff44b85-48dd-44df-af25-d9f0b7237f5a" src="https://github.com/user-attachments/assets/c6f9edde-fbfb-44ac-9290-33048b11b07e" />


# 🖥️ TH_DESK Server Manager (x64)
**โปรแกรมจัดการเซิร์ฟเวอร์รีโมทเดสก์ท็อปสำเร็จรูป รองรับ DDNS, แยกพอร์ต IN/OUT และเปิด Firewall อัตโนมัติ**  
*All-in-One Self-Hosted Server Manager for TH_DESK / RustDesk (Windows 64-bit)*

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0071FF?style=for-the-badge&logo=windows)](https://github.com/kuromineko/TH_DESK_Server)
[![Framework](https://img.shields.io/badge/.NET%2010-WPF%20Dark%20UI-512BD4?style=for-the-badge&logo=dotnet)](https://github.com/kuromineko/TH_DESK_Server)
[![License](https://img.shields.io/badge/License-AGPL--3.0-10B981?style=for-the-badge)](https://www.gnu.org/licenses/agpl-3.0.html)
[![Client Repo](https://img.shields.io/badge/Client%20Repo-TH__DESK-38BDF8?style=for-the-badge&logo=github)](https://github.com/kuromineko/TH_DESK)

</div>

---

## ✨ คุณสมบัติเด่น (Key Features)

* 📦 **All-in-One Embedded Binaries:** ฝังคอร์เซิร์ฟเวอร์ `hbbs.exe` (ID Server) และ `hbbr.exe` (Relay Server) รุ่น 64-bit ไว้ในตัวโปรแกรม เปิดใช้งานได้ทันทีในไฟล์เดียวโดยไม่ต้องตั้งค่า Command Line
* 🌐 **DDNS & Custom Port Forwarding (แยก IN / Out Port):** ออกแบบมาเพื่อการใช้งานร่วมกับเร้าเตอร์และ DDNS โดยเฉพาะ สามารถแยกตั้งค่า **IN Port (พอร์ตภายในเครื่องคอม)** และ **Out Port (พอร์ตภายนอกบนเร้าเตอร์/DDNS)** ได้อย่างอิสระ
* 🛡️ **Auto Windows Firewall:** เปิดพอร์ต TCP และ UDP ใน Windows Firewall ให้ตรงตามหมายเลขพอร์ตที่ตั้งไว้อัตโนมัติทันทีที่กดเริ่มระบบ
* ⚡ **Built-in Port Checker:** มีปุ่มทดสอบสถานะพอร์ตในตัว เช็กได้ทันทีว่าพอร์ตในเครื่องทำงานปกติและพอร์ตภายนอกทะลุเร้าเตอร์ (Port Forwarding) สำเร็จหรือไม่
* 📥 **Zero-Config Client Export:** ส่งออกไฟล์ตั้งค่าเครื่องลูกข่าย (`TH_DESK2.toml` และสคริปต์ติดตั้งคลิกเดียว `Setup_TH_DESK_Client.bat`) ให้ผู้ใช้งานปลายทางดับเบิลคลิกเชื่อมต่อเข้าเซิร์ฟเวอร์ได้ทันทีโดยไม่ต้องกรอก IP หรือ Public Key เอง
* 🚀 **Auto-Start on Windows Boot & System Tray:** รองรับการตั้งค่าเปิดระบบเซิร์ฟเวอร์อัตโนมัติเมื่อเปิดเครื่อง Windows (ผ่าน Task Scheduler สิทธิ์สูงสุด) พร้อมพับเก็บลงมุมขวาล่างข้างนาฬิกา (System Tray)
* 🇹🇭 **Bilingual Modern Dark UI (TH / EN):** หน้าตาโปรแกรมโทนมืด (Dark Mode) สไตล์เดียวกับตัวรีโมท รองรับการสลับภาษา **ไทย / English** แบบเรียลไทม์

---

## 🔌 การตั้งค่าพอร์ตและเร้าเตอร์ (Port Forwarding Guide)

โปรแกรมรองรับการจับคู่พอร์ตระหว่างพอร์ตในเครื่องคอมพิวเตอร์ (**IN Port**) และพอร์ตภายนอกเร้าเตอร์ (**Out Port**):

| บริการ (Service) | โปรโตคอล (Protocol) | IN Port (พอร์ตในเครื่อง Server) | Out Port (พอร์ตนอก DDNS) | หน้าที่การทำงาน |
| :--- | :---: | :---: | :---: | :--- |
| **ID Server (`hbbs`)** | **TCP + UDP** | เช่น `1555` *(และพอร์ตข้างเคียง ±2)* | เช่น `4997` | ระบบจับคู่เครื่อง รอรับการเชื่อมต่อและตรวจสอบ Key |
| **Relay Server (`hbbr`)** | **TCP Only** | เช่น `1556` | เช่น `4998` | ระบบส่งภาพสำรองกรณีเครือข่ายปลายทางไม่สามารถเชื่อมต่อแบบ P2P ตรงได้ |

> **วิธีตั้งค่าในเร้าเตอร์ (Router Port Forwarding):**
> 1. ชี้ **External Port (พอร์ตนอก)** ตามเลข `Out Port` มาที่ IP ของเครื่องเซิร์ฟเวอร์ ที่พอร์ต **Internal Port (พอร์ตใน)** ตามเลข `IN Port`
> 2. สำหรับ **ID Server (`hbbs`)** แนะนำให้เปิดทั้ง **TCP และ UDP**
> 3. กดปุ่ม **"⚡ เช็คพอร์ต"** บนหน้าโปรแกรมเพื่อตรวจสอบว่าพอร์ตทะลุเร้าเตอร์พร้อมใช้งาน

---

## 🚀 วิธีใช้งาน (Quick Start)

1. ดาวน์โหลดไฟล์ **`TH_DESK_Server.exe`** จากหน้า [Releases](https://github.com/kuromineko/TH_DESK_Server/releases)
2. เปิดโปรแกรม (สิทธิ์ Administrator) แล้วกรอกชื่อ **โดเมน DDNS หรือ Public IP** ของคุณ
3. ตั้งหมายเลข **IN Port (ในเครื่อง)** และ **Out Port (พอร์ตนอก)** ตามต้องการ
4. กดปุ่ม **"▶ เปิดระบบ Server + เปิด Firewall"** โปรแกรมจะคลายไฟล์เซิร์ฟเวอร์ เปิดพอร์ต Firewall และสร้างรหัส **Public Key** ให้อัตโนมัติ
5. กดปุ่ม **"📥 ส่งออกไฟล์ลูกข่าย"** เพื่อนำไฟล์ `Setup_TH_DESK_Client.bat` ไปรันที่เครื่องลูกข่าย (ตั้งค่าเชื่อมต่ออัตโนมัติโดยไม่ต้องกรอก IP/Key เอง)

---

## 🛠️ การคอมไพล์จากซอร์สโค้ด (Build from Source)

* **ความต้องการระบบ:** Windows 10/11 (x64), Visual Studio 2022/2026, .NET 10.0 SDK (`net10.0-windows`)
* เปิดไฟล์ `TH_DESK_Server.sln` ใน Visual Studio ตั้งค่าเป็น `Release | x64` แล้วสั่ง Build หรือ Publish แบบ `Single File` ได้ทันที

---

## 📜 เครดิตและสัญญาอนุญาต (Credits & License)

* **ผู้พัฒนาและดูแลโปรเจกต์ (Developer & Maintainer):** [kuromineko](https://github.com/kuromineko)
* **โปรเจกต์ฝั่งผู้ใช้งาน (Client Repository):** [kuromineko/TH_DESK](https://github.com/kuromineko/TH_DESK)
* **ซอร์สโค้ดต้นฉบับ (Upstream Projects):** พัฒนาต่อยอดและทำงานร่วมกับ [RustDesk](https://github.com/rustdesk/rustdesk) และ [RustDesk Server OSS (`hbbs` / `hbbr`)](https://github.com/rustdesk/rustdesk-server)
* **สัญญาอนุญาต (License):** เผยแพร่ภายใต้สัญญาอนุญาตสาธารณะ **[GNU Affero General Public License v3.0 (AGPL-3.0)](https://www.gnu.org/licenses/agpl-3.0.html)** เปิดเผยซอร์สโค้ดและแจกจ่ายให้ใช้งานได้ฟรี
