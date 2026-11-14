# راهنمای یکپارچه‌سازی سامانه پیامک آتیه داده‌پرداز (payamSMS) — وب‌سرویس نسخه ۲

این سند «متن ساده»ی راهنمای رسمی ارائه‌شده از سوی سرویس‌دهنده است، به‌همراه قرارداد فنیِ استخراج‌شده از فایل
WSDL زنده. هدف، از بین رفتن نیاز به بازخوانی PDF در مراجعات بعدی و درست‌کردن پیاده‌سازی
`ISmsGateway` بر پایه‌ی قرارداد واقعی است، نه حدس.

| مورد | مقدار |
|---|---|
| سند | سیستم ارائه خدمات پیامک آتیه داده‌پرداز از طریق Web Service — ویژه برنامه‌نویسان |
| نسخه سند | ۱.۲ |
| تاریخ سند | تیر ۱۴۰۳ |
| فایل مرجع | `payamSMS+soapV2_14030515.pdf` (خارج از مخزن؛ این فایل، متن آن است) |
| آدرس WSDL اعلامی در سند | `https://www.payamsms.com/services/v2/service.wsdl` |
| آدرس نقطه اتصال در WSDL زنده | `https://www.payamsms.com/services/v2/index.php` |

> **قانون منبع‌خوانی:** بخش‌های ۱ تا ۵ عینِ مفاد سند سرویس‌دهنده‌اند. بخش ۶ نتایج بررسیِ مستقیم فایل
> WSDL است و اگر با جایی از سند در تضاد باشد، **WSDL مرجع اجراست** (چون سرور همان را پاسخ می‌دهد).
> بخش ۷ وضعیت کد فعلی همین مخزن را نسبت به همین قرارداد توضیح می‌دهد.

---

## ۱. مقدمه

هدف از تهیه این نوشتار، مشخص نمودن نحوه استفاده از سیستم ارائه خدمات پیامک از طریق Web Service است.

## ۲. تعاریف، اصطلاحات و اختصارات

- **پیام:** منظور پیامک است.
- **پیام‌رسان (Proxy):** مجموعه سخت‌افزاری/نرم‌افزاری که با پروتکل SMPP به سامانه مرکزی پیامک مخابرات (SMSC) متصل است.
- **نیازمندی‌های عملیاتی (Functional Requirements):** مشخص می‌کند سیستم چه کارهایی انجام می‌دهد؛ به عبارت دیگر رفتار و عملکرد سیستم.
- **نیازمندی‌های غیرعملیاتی (Non-functional Requirements):** معیارهایی که چگونگی عملکرد سیستم را مشخص می‌کند.
- **وب‌سرویس:** در اینجا نوع خاصی از آن که روی پروتکل HTTP و به روش RPC کار می‌کند.
- **دسترسی برنامه‌نویسی (Programmatic Access):** استفاده از سیستم به کمک واسط Web Service.
- **واسط کاربر (User Interface):** استفاده از سیستم از طریق فرم‌ها و صفحات وب.
- **پیام بلند (Long Message):** پیامی که به‌صورت چندین بلوک پیام کوچک فرستاده می‌شود، به‌شکلی که هنگام دریافت توسط گیرنده یک پیام دیده شود.
- **سرفصل داده‌های خاص استفاده‌کننده (UDH):** بخشی از پیام کوتاه، معمولاً در پیام‌های باینری/خاص/طولانی، به‌عنوان راهنمای پردازش متن اصلی.
- **سیستم کدگذاری داده‌ها (DCS):** بخشی از پیام حاوی اطلاعات لازم درباره چگونگی کد کردن متن اصلی.
- **قطعه پیام (Piece):** هر تکه از یک پیام بلند که جداگانه ارسال می‌شود.
- **دسترسی به سرویس‌دهنده:** اولین قدم برای فراخوانی متدها، دسترسی به قرارداد WSDL است که همه متدها، تعداد و نوع پارامترهای آن‌ها در آن تعریف شده است. بسیاری از frameworkها با داشتن همین قرارداد می‌توانند کد لازم را به‌صورت خودکار تولید کنند.

## ۳. متدهای ارائه‌شده در سند

### ۳.۱ ارسال یک پیام به یک یا چند شماره — `Send`

```
SendResult[] Send(string organization, string username, string password,
                  string srcNumber, string body, string[] destNo)
```

| نام پارامتر | نوع | شرح |
|---|---|---|
| `organization` | string | نام سازمان |
| `username` | string | شناسه کاربری |
| `password` | string | کلمه عبور جاری |
| `srcNumber` | string | شماره فرستنده — یکی از شمارههای اختصاص‌داده‌شده به مشتری، مانند `۹۸2000000` |
| `body` | string | متن پیام |
| `destNo` | string[] | آرایه شماره‌ها؛ حتماً عدد، باید با کد کشور شروع شود و حداکثر ۱۲ رقم |

نکته‌های سند:
- قبل از ارسال، اعتبار مشتری بررسی می‌شود؛ در صورت ناکافی بودن، نتیجه با کد وضعیت خطای مناسب برمی‌گردد.
- در هر فراخوانی، طول `destNo` (تعداد دریافت‌کنندگان) نباید از **۲۵۰** بیشتر باشد.
- فارسی یا لاتین بودن پیام به‌صورت خودکار توسط سیستم تشخیص داده می‌شود.

**مقدار بازگشتی:** آرایه‌ای از `SendResult`.

| نام پارامتر | نوع | شرح |
|---|---|---|
| `ID` | string | شماره یکتای پیام ارسالی — برای رهگیری وضعیت در متد `getStatuses_OriginalID` استفاده می‌شود |
| `Mobile` | string | شماره موبایلی که فیلد `ID` به آن تعلق دارد |

اگر در `Send` خطایی رخ دهد، آرایه بازگشتی تنها شامل **یک سطر** خواهد بود که متن و شماره خطا در فیلد `ID` قرار می‌گیرد.

### ۳.۲ دریافت پیام‌های ورودی — `ViewReceive`

```
ReceiveResult[] ViewReceive(string organization, string username, string password,
                            string number, string id)
```

| نام پارامتر | نوع | شرح |
|---|---|---|
| `organization` | string | نام سازمان |
| `username` | string | شناسه کاربری |
| `password` | string | کلمه عبور جاری |
| `number` | string | شماره گیرنده — یکی از شمارههای اختصاص‌داده‌شده به مشتری، مانند `۹۸2000000` |
| `id` | string | شماره یکتای پیام ورودی |

رفتار `id` طبق سند:
- اگر خالی باشد، سیستم به‌صورت خودکار **تا ۹۰** پیام تازه‌ی خوانده‌نشده را برمی‌گرداند و وضعیت آن‌ها را به «خوانده‌شده» تغییر می‌دهد.
- اگر شماره پیامی داده شود، همه پیام‌های واردشده بعد از آن شماره را تا سقف ۹۰ خروجی برمی‌گرداند.
- برای دریافت اولین پیام، عدد `0` را در فیلد `id` بفرستید.

**مقدار بازگشتی:** آرایه‌ای از `ReceiveResult`.

| نام پارامتر | نوع | شرح |
|---|---|---|
| `From` | string | شماره فرستنده پیام |
| `To` | string | شماره مقصد |
| `Body` | string | متن پیام ورودی |
| `Date` | string | تاریخ دریافت پیام |
| `ID` | string | شماره‌ای که از طرف سیستم به این پیام ورودی اختصاص داده می‌شود |

در صورت خطا در `ViewReceive` نیز آرایه بازگشتی یک سطر است و متن/شماره خطا در فیلد `ID` قرار می‌گیرد.

### ۳.۳ دریافت میزان اعتبار باقیمانده — `Balance`

```
string Balance(string organization, string username, string password)
```

پارامترها همان سه مقدار احراز هویت بالاست. خروجی یک رشته عددی است که **میزان اعتبار ریالی باقیمانده** را نشان می‌دهد.

### ۳.۴ ارسال چند‌به‌چند — `SendMultiple`

شکل کلی‌تر `Send`؛ امکان ارسال پیام‌های متفاوت به گیرندگان متفاوت با شمارههای ارسال‌کننده متفاوت.

```
MultipleSendResult[] SendMultiple(string organization, string username, string password,
                                  string[] srcNumbers, string[] bodys, string[] destNos,
                                  long[] checkingMessageIds)
```

| نام پارامتر | نوع | شرح |
|---|---|---|
| `organization` | string | نام سازمان |
| `username` | string | شناسه کاربری |
| `password` | string | کلمه عبور جاری |
| `srcNumbers` | string[] | آرایه‌ای از شمارههای اختصاص‌داده‌شده به مشتری، مانند `۹۸2000000` |
| `bodys` | string[] | آرایه‌ای از متن پیام‌ها |
| `destNos` | string[] | آرایه‌ای از گیرندگان |
| `checkingMessageIds` | long[] | آرایه شناسه‌های منحصربه‌فرد هر پیام ارسالی که **از سمت کاربر مقداردهی می‌شود** و بعداً در متد `getStatuses` برای دریافت وضعیت به کار می‌رود |

**مقدار بازگشتی:** آرایه‌ای از `MultipleSendResult`.

| نام پارامتر | نوع | شرح |
|---|---|---|
| `ID` | string | شماره یکتای پیام ارسالی — برای رهگیری وضعیت در `getStatuses_OriginalID` |

در صورت خطا، آرایه بازگشتی شامل شناسه خطا در فیلد `ID` خواهد بود.

### ۳.۵ دریافت وضعیت پیام‌ها از طریق `checkingIds` — `getStatuses`

مشتری در `SendMultiple` برای هر پیام یک شناسه یکتا مقداردهی می‌کند؛ این متد با دریافت همان شناسه‌ها، وضعیت متناظر را برمی‌گرداند.

```
string[] getStatuses(string organization, string username, string password, long[] checkingIds)
```

| نام پارامتر | نوع | شرح |
|---|---|---|
| `organization` | string | نام سازمان |
| `username` | string | شناسه کاربری |
| `password` | string | کلمه عبور جاری |
| `checkingIds` | long[] | آرایه شناسه‌های منحصربه‌فردی که مشتری به هر پیام نسبت داده است |

**مقدار بازگشتی:** آرایه‌ای از `DeliveryResult` که هر خانه سه پارامتر دارد:

| پارامتر | نوع | توضیح |
|---|---|---|
| `ID` | String | شناسه تولیدشده توسط سرور |
| `checkID` | String | شناسه تولیدشده توسط مشتری |
| `status` | String | وضعیت پیام |

در صورت خطا، آرایه بازگشتی در همان سطر شناسه خطا را حمل می‌کند.

### ۳.۶ دریافت وضعیت از طریق شناسه‌های سرور — `getStatuses_OriginalID`

```
string[] getStatuses_OriginalID(string organization, string username, string password, long[] ids)
```

| نام پارامتر | نوع | شرح |
|---|---|---|
| `organization` | string | نام سازمان |
| `username` | string | شناسه کاربری |
| `password` | string | کلمه عبور جاری |
| `ids` | long[] | آرایه شناسه‌های منحصربه‌فردی که **توسط سرور** به پیام‌ها نسبت داده شده است |

مقدار بازگشتی و ساختار `DeliveryResult` و رفتار خطا، مشابه بند ۳.۵ است.

## ۴. محدودیت‌های جمع‌بندی‌شده از سند

- حداکثر **۲۵۰** دریافت‌کننده در هر فراخوانی.
- شماره‌ها باید با **کد کشور** شروع شوند و حداکثر **۱۲ رقم** باشند.
- **اعتبار قبل از ارسال** بررسی می‌شود.
- تشخیص خودکار فارسی/لاتین متن پیام.
- سقف **۹۰** پیام در هر فراخوانی `ViewReceive` و تغییر خودکار وضعیت آن‌ها به خوانده‌شده.
- واحد بازگشتی `Balance`: **ریال**.
- الگوی خطا: در متدهای ارسال/دریافت، آرایه نتیجه به یک سطر تقلیل می‌یابد و کد/متن خطا در فیلد `ID` می‌نشیند.

## ۵. جدول کدهای خطا

سند ۲۱ کد را فهرست کرده است. توضیح فارسی برای همه آمده؛ معادل انگلیسی (همان‌طور که در جدول سند چاپ شده) فقط برای ۱۶ مورد اول موجود است و برای پنج مورد آخر در سند خالی است.

| کد خطا | شرح فارسی (مطابق سند) | معادل انگلیسی در سند |
|---|---|---|
| `E1` | نام کاربری یا کلمه عبور وارد نشده است | Security HTTP Header Not Set |
| `E2` | ورود تمامی پارامترها الزامی است | All Parameters Is Require |
| `E3` | پارامتر ورودی باید از نوع رشته باشد | Entered Parameters Most String be as |
| `E4` | دسترسی غیرمجاز به سیستم | Access Forbidden |
| `E5` | نام کاربری یا کلمه عبور صحیح نیست | Authentication Failed |
| `E107` | تعداد گیرندگان مشخص‌شده بیش از ۲۵۰ گیرنده است | Request Size Out Of Range |
| `E223` | شماره اختصاصی واردشده نامعتبر است | Source Number not valid |
| `E211` | طول متن پیام بیش از حد مجاز است | Message Long Out Of Range |
| `E6` | اعتبار برای ارسال کافی نیست | Not Enough Credit |
| `E7` | پیام ورودی تازه‌ی خوانده‌نشده‌ای وجود ندارد | No New Message |
| `E8` | شناسه واردشده پیام جهت دریافت وضعیت نامعتبر است | ID Not Correct |
| `E305` | شناسه واردشده پیام جهت دریافت وضعیت وجود ندارد | Message ID is not Valid |
| `E65` | مقداردهی شناسه پیام الزامی است | Message Id Is Require |
| `E66` | مقداردهی شماره اختصاصی موردنظر الزامی است | srcNumber Is Require |
| `E43` | شماره گیرنده نامعتبر است | Recipient Number is not Valid |
| `E766` | متن پیام خالی است | Message Body is Empty |
| `E999` | شماره منقضی شده است | *(در سند ذکر نشده)* |
| `E998` | پکیج منقضی شده است | *(در سند ذکر نشده)* |
| `E303` | هیچ شماره گیرنده صحیحی وارد نشده | *(در سند ذکر نشده)* |
| `E132` | متن واردشده شامل کلمات حساس است | *(در سند ذکر نشده)* |
| `E800` | لیست سیاه | *(در سند ذکر نشده)* |

---

## ۶. قرارداد فنی همان‌طور که از WSDL زنده استخراج شده

منبع: `https://www.payamsms.com/services/v2/service.wsdl` (بازبینی مستقیم، HTTP 200). این بخش بر بخش‌های
بالا اولویت دارد، چون همان چیزی است که سرور پاسخ می‌دهد.

### ۶.۱ شناسه‌های سرویس

| مورد | مقدار |
|---|---|
| `targetNamespace` | `urn:SMSAPIwsdl` |
| `portType` | `SMSAPIPortType` |
| `binding` | `SMSAPIBinding` |
| `port` | `SMSAPIPort` |
| `soap:address location` | `https://www.payamsms.com/services/v2/index.php` |
| سبک اتصال | `soap:binding style="rpc"` با `transport="http://schemas.xmlsoap.org/soap/http"` |
| سبکEnvelope | SOAP 1.1 — آرایه‌ها با `SOAP-ENC:arrayType` (سبک کدگذاری‌شده قدیمی) |

نکته مهم: سبک **RPC/encoded** است، نه Document/Literal. یعنی بدن پیام مستقیماً نام متد را دارد و
پارامترها به‌صورت فرزندهای هم‌سطح آن می‌آیند (مثلاً `<Send>` شامل `<organization>`، `<username>` …).

### ۶.۲ عملیات و `soapAction`

WSDL هفت عملیات دارد؛ در سند فقط شش مورد آن‌ها توضیح داده شده است.

| عملیات WSDL | `soapAction` | ورودی | خروجی |
|---|---|---|---|
| `Send` | `urn:SMSAPIwsdl#Send` | `SendRequest` | `SendResultArray` |
| `SendMultiple` | `urn:SMSAPIwsdl#Send` ⚠ | `SendMultipleRequest` | `MultipleSendResultArray` |
| `ViewReceive` | `urn:SMSAPIwsdl#ViewReceive` | `ViewReceiveRequest` | `ReceiveResultArray` |
| `getStatuses` | `urn:SMSAPIwsdl#getStatuses` | `getStatusesRequest` | `DeliveryResultArray` |
| `getStatuses_OriginalID` | `urn:SMSAPIwsdl#getStatuses` ⚠ | `getStatuses_OriginalIDRequest` | `DeliveryResultArray` |
| `Acknowledge` | `urn:SMSAPIwsdl#Acknowledge` | `AcknowledgeRequest` | `Statuses` |
| `Balance` | `urn:SMSAPIwsdl#return account balance` ⚠ | `BalanceRequest` | `string` |

⚠ سه واقعیتی که فقط از WSDL دیده می‌شود:
1. `SendMultiple` همان `soapAction` متد `Send` را دارد (`#Send`)، نه action مستقل.
2. `getStatuses_OriginalID` نیز همان `#getStatuses` را دارد.
3. action متد `Balance` رشته‌ی عجیب `urn:SMSAPIwsdl#return account balance` است (فاصله دارد، CamelCase نیست).

### ۶.۳ ترتیب دقیق پارامترها در مِسیج‌ها

ترتیب زیر همان ترتیب اجباری فرزندهای envelope است:

- `SendRequest`: `organization:string`, `username:string`, `password:string`, `srcNumber:string`, `body:string`, `destNo:string[]`
- `SendMultipleRequest`: `organization:string`, `username:string`, `password:string`, `srcNumbers:string[]`, `bodys:string[]`, `destNos:string[]`, `checkingMessageIds:long[]`
- `ViewReceiveRequest`: `organization:string`, `username:string`, `password:string`, `number:string`, `id:string`
- `getStatusesRequest`: `organization:string`, `username:string`, `password:string`, `checkingIds:long[]`
- `AcknowledgeRequest`: `organization:string`, `username:string`, `password:string`, `checkingIds:long[]` → بازگشت `Statuses` (آرایه رشته)
- `getStatuses_OriginalIDRequest`: `organization:string`, `username:string`, `password:string`, `Ids:long[]` (دقت کنید: نام پارامتر با **I بزرگ** و جمع: `Ids`)
- `BalanceRequest`: `organization:string`, `username:string`, `password:string` → بازگشت `return:string`

### ۶.۴ انواع مرکب، آن‌گونه که WSDL تعریف کرده

| نوع | فیلدها |
|---|---|
| `SendResult` | `ID`, `Mobile` (هر دو `xsd:string`) |
| `MultipleSendResult` | `ID` |
| `ReceiveResult` | `ID`, `From`, `To`, `Date`, `Body` (همه `xsd:string`) |
| `DeliveryResult` | `ID`, `CheckID`, `Status` — نام فیلد در WSDL **`CheckID`** است (سند فارسی `checkID` نوشته) |
| `Destinations` / `srcNumbers` / `bodys` / `checkingMessageIds` / `Statuses` | آرایه‌های `SOAP-ENC` به ترتیب `string[]`, `string[]`, `string[]`, `long[]`, `string[]` |

### ۶.۵ `Acknowledge`

در سند PDF هیچ توضیحی ندارد، اما در WSDL تعریف شده و امضای آن دقیقاً مثل `getStatuses` است با این
تفاوت که خروجی‌اش `Statuses` (آرایه رشته) است، نه `DeliveryResult`. قبل از استفاده در کد تولیدی،
رفتارش باید با یک فراخوان آزمایشی روی محیط سرویس‌دهنده تأیید شود.

### ۶.۶ نمونه envelope برای `Send`

```xml
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
               xmlns:tns="urn:SMSAPIwsdl"
               xmlns:xsd="http://www.w3.org/2001/XMLSchema"
               xmlns:SOAP-ENC="http://schemas.xmlsoap.org/soap/encoding/"
               SOAP-ENV:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
  <soap:Body>
    <tns:Send>
      <organization xsi:type="xsd:string">NAME</organization>
      <username xsi:type="xsd:string">USER</username>
      <password xsi:type="xsd:string">PASS</password>
      <srcNumber xsi:type="xsd:string">9820000000</srcNumber>
      <body xsi:type="xsd:string">متن پیام</body>
      <destNo xsi:type="SOAP-ENC:Array" SOAP-ENC:arrayType="xsd:string[1]">
        <item>989120000000</item>
      </destNo>
    </tns:Send>
  </soap:Body>
</soap:Envelope>
```

`Content-Type: text/xml; charset=utf-8` و `SOAPAction: "urn:SMSAPIwsdl#Send"`.

---

## ۷. وضعیت پیاده‌سازی در این مخزن

مرجع کد: [src/NovaFleet.Infrastructure/Sms/PayamSmsGateway.cs](file:///D:/Projects/NovaFleet/Nova-Fleet-Management/src/NovaFleet.Infrastructure/Sms/PayamSmsGateway.cs)،
[src/NovaFleet.Infrastructure/Sms/SmsGatewayRouter.cs](file:///D:/Projects/NovaFleet/Nova-Fleet-Management/src/NovaFleet.Infrastructure/Sms/SmsGatewayRouter.cs)

| موضوع | قرارداد payamSMS v2 | کد این مخزن |
|---|---|---|
| namespace | `urn:SMSAPIwsdl` | همان، با پیشوند `tns` روی ریشه |
| سبک | RPC/encoded | envelope دستی با `soap:encodingStyle` و پارامترهای بی‌namespace |
| عملیات ارسال | `Send` / `SendMultiple` | `Send` (یک گیرنده در هر فراخوانی؛ صف داخلی، گیرنده‌به‌گیرنده است) |
| عملیات وضعیت | `getStatuses` / `getStatuses_OriginalID` | `getStatuses_OriginalID` با پارامتر `Ids` از نوع `xsd:long[n]` |
| اعتبار | `Balance` | `Balance` با `soapAction` کاملِ `urn:SMSAPIwsdl#return account balance` |
| `soapAction` | `urn:SMSAPIwsdl#Send`, `#getStatuses`, … | همان‌ها، با نقل‌قول داخل مقدار هدر |
| پارامترها | `organization`, `username`, `password`, `srcNumber`, `body`, `destNo` | همان ترتیب، بدون افزودن یا جابه‌جایی |
| کدهای خطا | ۲۱ کد بخش ۵ | `PayamSmsFaults`؛ دائم/موقت جدا شده‌اند و کد ناشناخته موقت می‌ماند |
| شماره گیرنده | کد کشور + حداکثر ۱۲ رقم | `NormalizeDestination` (`0912…`، `912…`، `0098…`، `+98…` → `98912…`) |
| پیام ورودی | `ViewReceive`, `Acknowledge` | استفاده نمی‌شود؛ سامانه پیامک فقط خروجی دارد |
| چک‌لیست بخش ۸ | — | پیاده شده؛ فقط `SendMultiple` عمداً استفاده نشده (توضیح زیر) |

سه نکته که هنگام خواندن کد باید دانست:
۱) آدرس، سازمان، کاربر، فرستنده، timeout و حالت درگاه از پایگاه داده خوانده می‌شوند و
**رمز از انبار اسرار** (`GetSecretAsync`) می‌آید، پس تغییرشان نیاز به restart ندارد.
۲) شناسه برگشتی سرویس در `ProviderReference` ذخیره می‌شود و همان مقدار بعداً با
`getStatuses_OriginalID` رهگیری می‌شود؛ بنابراین سقف ۲۵۰ گیرنده ساختاراً نقض نمی‌شود.
۳) هر پاسخی که سرویس‌دهنده درباره‌اش قطعی نگفته (وضعیت ناشناخته، خطای تفسیرنشده،
HTTP بدون fault) «موقت» یا «نامشخص» می‌ماند، نه تحویل‌شده.

## ۸. چک‌لیست پیاده‌سازی

- [x] `Endpoint` از `appsettings` به تنظیمات runtime (دیتابیس + Setup) منتقل شود.
- [x] `organization` به پارامترهای سرویس افزوده شود.
- [x] `namespace` و نام عملیات‌ها طبق بخش ۶ اصلاح شود.
- [ ] `SendMultiple` با `checkingMessageIds` تولیدشده از سمت ما — **عمداً انجام نشده**:
      صف این سامانه گیرنده‌به‌گیرنده است و `Send` شناسه سرور برمی‌گرداند، پس شناسه رهگیری
      داخلی از همان ابتدا وجود دارد. اگر روزی ارسال دسته‌ای لازم شد، این مورد بازمی‌گردد.
- [x] محدودیت ۲۵۰ گیرنده قبل از فراخوانی اعمال شود — با یک گیرنده در هر فراخوانی نقض نمی‌شود.
- [x] شماره‌ها به فرمت کد کشور + حداکثر ۱۲ رقم نرمال‌سازی شوند (`0912…` → `98912…`).
- [x] جدول ۲۱ کد خطا به یک نگاشت قابل‌مطالعه تبدیل شود؛ خطاهای «تماس مجدد نکن» از «تلاش دوباره» جدا شوند.
- [x] `sms.max-attempts` واقعاً به retry وصل شود (در ساخت ردیف صف و در سقف تلاش‌های مجدد).
- [x] یک حالت «API/REST» در کنار «WSDL/SOAP» برای انتخاب کاربر قرار گیرد — قرارداد REST از
      پروسیجر مرجع `SP_SendSMS` و کاوش زنده استخراج و پیاده شد
      ([PayamSmsRestGateway.cs](../../src/NovaFleet.Infrastructure/Sms/PayamSmsRestGateway.cs))؛
      سند کامل در [payam-sms-rest.md](payam-sms-rest.md). تنها محدودیت: درگاه REST متد وضعیت
      تحویل ندارد، پس در حالت `Api` پیام پذیرفته‌شده باقی می‌ماند.

آزمون‌ها: [tests/NovaFleet.UnitTests/Sms/PayamSmsGatewayTests.cs](file:///D:/Projects/NovaFleet/Nova-Fleet-Management/tests/NovaFleet.UnitTests/Sms/PayamSmsGatewayTests.cs)
با `HttpMessageHandler` ساختگی، شکل پاکت، `SOAPAction`، ترتیب پارامترها، کدگذاری آرایه‌ها،
بدنه فارسی UTF-8، طبقه‌بندی خطاها و انتخاب حالت از دیتابیس را می‌سنجند.

