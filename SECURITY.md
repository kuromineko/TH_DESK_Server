# นโยบายด้านความปลอดภัย / Security Policy

## 🇹🇭 ภาษาไทย (Thai)

### แนวทางการรักษาความปลอดภัยในการใช้งาน TH_DESK Server
* **เก็บรักษาไฟล์ Private Key (`id_ed25519`) เป็นความลับ:** เผยแพร่เฉพาะไฟล์หรือรหัส **Public Key (`id_ed25519.pub`)** ให้กับเครื่องลูกข่ายเท่านั้น ห้ามส่งไฟล์ `id_ed25519` (ที่ไม่มีนามสกุล `.pub`) ให้ผู้อื่นโดยเด็ดขาด
* **การตั้งค่าพอร์ต (Port Forwarding):** เปิดเฉพาะพอร์ตที่จำเป็นต้องใช้งานสำหรับ `hbbs` (TCP+UDP) และ `hbbr` (TCP) บนเร้าเตอร์ของคุณ
* **เปลี่ยนคีย์ทันทีเมื่อสงสัยว่าข้อมูลรั่วไหล:** คุณสามารถกดปุ่ม **"🔑 ลบและสร้าง Public Key ใหม่"** ในหน้าตั้งค่าระบบของโปรแกรม **TH_DESK Server** ได้ตลอดเวลา

### การรายงานช่องโหว่ด้านความปลอดภัย
* หากพบช่องโหว่ในโปรเจกต์ **TH_DESK Server** กรุณาแจ้งผ่าน [GitHub Issues](https://github.com/kuromineko/TH_DESK_Server/issues) หรือแท็บ **Security Advisories** ของ [kuromineko](https://github.com/kuromineko)
* สำหรับช่องโหว่ที่เกิดจากคอร์เซิร์ฟเวอร์ต้นฉบับ (`hbbs` / `hbbr`) สามารถตรวจสอบควบคู่ไปยังโปรเจกต์ต้นน้ำได้ที่ [RustDesk Server Repository](https://github.com/rustdesk/rustdesk-server)

---

## 🇬🇧 English

### Security Best Practices for Self-Hosting TH_DESK Server
* **Keep Your Private Key (`id_ed25519`) Secret:** Only distribute the **Public Key (`id_ed25519.pub`)** to client machines. Never share the private `id_ed25519` file.
* **Port Forwarding:** Only forward the required ports for `hbbs` (TCP+UDP) and `hbbr` (TCP) on your router.
* **Key Rotation:** If you suspect your server key has been compromised, click **"🔑 Regenerate KeyPair"** in the **Settings** tab of **TH_DESK Server Manager** to invalidate old keys and generate a fresh pair immediately.

### Reporting a Vulnerability
* To report a security issue in **TH_DESK Server**, please open a report via [GitHub Issues](https://github.com/kuromineko/TH_DESK_Server/issues) or the **Security Advisories** tab on [kuromineko's repository](https://github.com/kuromineko/TH_DESK_Server/security).
* For vulnerabilities related to the core server binaries (`hbbs` / `hbbr`), please also refer to the [Upstream RustDesk Server Repository](https://github.com/rustdesk/rustdesk-server).
