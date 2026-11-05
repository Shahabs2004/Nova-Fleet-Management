/*
  ناوگان نوین — داده نمونه برای بررسی داشبوردها، تایم‌لاین، گزارش‌ها و صف پیامک.

  هیچ رمزی در این فایل نیست؛ هش رمز کاربرهای نمونه از بیرون و با متغیر sqlcmd داده می‌شود.

  اجرا (پاک‌سازی قبلی و سپس بارگذاری):
    sqlcmd -S <host> -d NovaFleetDb -U sa -P "<رمز>" -i scripts/seed-demo.sql -v CleanMode=1 DemoPasswordHash="<hash>"
  اجرا (بارگذاری تنها):
    sqlcmd -S <host> -d NovaFleetDb -U sa -P "<رمز>" -i scripts/seed-demo.sql -v CleanMode=0 DemoPasswordHash="<hash>"

  در CleanMode=1 همه مأموریت‌ها، درخواست‌های تغییر و صف پیامک پاک می‌شوند (نه فقط رکوردهای نمونه)،
  چون این پایگاه‌داده فقط برای نمایش و تست داده است. سایر رکوردهای نمونه شناسه‌ای در بازه
  D0DE0000-... دارند و با همان پیش‌شونده پاک می‌شوند.
  زمان‌ها بر پایه ساعت تهران (+۳:۳۰) ساخته و به UTC تبدیل می‌شوند تا تاریخ جلالی نمایشی درست بماند.
  دامنه فقط ساعت کامل به وقت تهران را می‌پذیرد؛ مأموریت‌ها از یک ساعت تا چند روز طول می‌کشند و روی
  بازه دو ماه قبل تا دو ماه آینده پخش می‌شوند.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Clean int = CAST(N'$(CleanMode)' AS int);

BEGIN TRANSACTION;

IF @Clean = 1
BEGIN
    DELETE FROM SmsQueueItems;
    DELETE FROM MissionChangeRequests;
    DELETE FROM Missions;
    DELETE FROM UserRoles             WHERE UserId LIKE 'D0DE0003%';
    DELETE FROM UserPermissions       WHERE UserId LIKE 'D0DE0003%';
    DELETE FROM Users                 WHERE Id LIKE 'D0DE0003%';
    DELETE FROM Drivers               WHERE Id LIKE 'D0DE0000%';
    DELETE FROM Vehicles              WHERE Id LIKE 'D0DE0001%';
    DELETE FROM Locations             WHERE Id LIKE 'D0DE000B%';
    DELETE FROM OrganizationUnits     WHERE Id LIKE 'D0DE000A%';
    DELETE FROM SystemSettings        WHERE [Key] = N'seed.demo.version';
END

IF EXISTS (SELECT 1 FROM SystemSettings WHERE [Key] = N'seed.demo.version')
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'داده نمونه پیش‌تر بارگذاری شده است. برای بارگذاری مجدد با CleanMode=1 اجرا کنید.';
    RETURN;
END

DECLARE @UtcNow       datetimeoffset  = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
DECLARE @TehranDay    datetime        = CAST(CAST(DATEADD(MINUTE, 210, SYSUTCDATETIME()) AS date) AS datetime);
DECLARE @PasswordHash nvarchar(500)   = N'$(DemoPasswordHash)';
DECLARE @Admin        nvarchar(100)   = ISNULL((SELECT TOP 1 Username FROM Users WHERE IsLocalAdmin = 1 ORDER BY CreatedAtUtc), N'admin');
DECLARE @DriverCount  int = 30;
DECLARE @VehicleCount int = 34;
DECLARE @MissionsPerDriver int = 26;
DECLARE @RequestCount int = 60;
DECLARE @SpanDays     int = 60;
DECLARE @NumberOffset int = 100;

/* ---------- جدول‌های کمکی ---------- */

DECLARE @Numbers table (n int primary key);
INSERT INTO @Numbers (n)
SELECT TOP 400 ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM sys.columns AS a CROSS JOIN sys.columns AS b;

DECLARE @FirstNames table (idx int identity(1,1), value nvarchar(100));
INSERT INTO @FirstNames (value) VALUES
  (N'علی'), (N'رضا'), (N'محمد'), (N'حسین'), (N'مهدی'), (N'قاسم'), (N'یوسف'), (N'ایوب'),
  (N'هادی'), (N'کامران'), (N'بهروز'), (N'فرهاد'), (N'سامان'), (N'وحید'), (N'امیر'), (N'سعید');

DECLARE @LastNames table (idx int identity(1,1), value nvarchar(100));
INSERT INTO @LastNames (value) VALUES
  (N'مرادی'), (N'رضایی'), (N'کریمی'), (N'احمدی'), (N'موسینی'), (N'قربانی'), (N'جعفری'), (N'صادقی'),
  (N'هاشمی'), (N'نوروزی'), (N'کاظمی'), (N'یزدانی'), (N'بهرامی'), (N'سلطانی'), (N'موسوی'), (N'فرهادی'),
  (N'رحیمی'), (N'زارعی');

DECLARE @Brands table (idx int identity(1,1), brand nvarchar(100), model nvarchar(100));
INSERT INTO @Brands (brand, model) VALUES
  (N'ایران‌خودرو', N'پیکان'), (N'ایران‌خودرو', N'سمند'), (N'ایران‌خودرو', N'پژو ۴۰۵'),
  (N'ایران‌خودرو', N'دنا پلاس'), (N'سایپا', N'پرایا'), (N'سایپا', N'تیبا'),
  (N'زامیاد', N'کامیون بنز'), (N'آویون', N'نیسان زامیاد'), (N'بهمن', N'وانت'),
  (N'فوتون', N'کشنده'), (N'مرسدس بنز', N'اتوبوس'), (N'هیوندای', N'مینی‌بوس'),
  (N'ایران‌خودرو', N'پژو پارس'), (N'سایپا', N'شاهین'), (N'چری', N'تیگو ۷'),
  (N'امویکو', N'کمره ۶۵۰'), (N'زامیاد', N'کمپر'), (N'ایسوزو', N'کامیون ۳ تن'),
  (N'فورد', N'ترانزیت'), (N'تویوتا', N'هایلوکس'), (N'میتسوبیشی', N'لنسر'),
  (N'رنو', N'تندر'), (N'مازدا', N'۳'), (N'کیام', N'کرنر');

/* حروف مجاز پلاک ملی — همان فهرست IranianLicensePlate */
DECLARE @PlateLetters table (idx int identity(1,1), value nvarchar(5));
INSERT INTO @PlateLetters (value) VALUES
  (N'ب'), (N'ج'), (N'د'), (N'س'), (N'ص'), (N'ط'), (N'ع'),
  (N'ق'), (N'ل'), (N'م'), (N'ن'), (N'و'), (N'ه'), (N'ی');

DECLARE @IranCodes table (idx int identity(1,1), value nvarchar(5));
INSERT INTO @IranCodes (value) VALUES
  (N'11'), (N'12'), (N'14'), (N'17'), (N'21'), (N'23'), (N'26'), (N'31'), (N'35'), (N'41');

DECLARE @Units table (idx int identity(1,1), value nvarchar(200));
INSERT INTO @Units (value) VALUES
  (N'معاونت فنی'), (N'واحد لجستیک'), (N'واحد توزیع'), (N'امور اداری'), (N'واحد بازرگانی'), (N'واحد پروژه‌ها');

DECLARE @Places table (idx int identity(1,1), value nvarchar(200));
INSERT INTO @Places (value) VALUES
  (N'انبار مرکزی'), (N'پلاتو ۴'), (N'پلاتو ۷'), (N'ادارات مرکزی'), (N'کارخانه شهرک صنعتی'),
  (N'پایانه جنوب'), (N'فرودگاه امام'), (N'بندرعباس'), (N'انبار قم'), (N'دفتر کاشان'),
  (N'پروژه اراک'), (N'ایستگاه گچساران'), (N'کارخانه دلیجان'), (N'انبار مس سرچشمه');

DECLARE @Titles table (idx int identity(1,1), value nvarchar(200));
INSERT INTO @Titles (value) VALUES
  (N'انتقال بار به انبار مرکزی'), (N'توزیع محموله فصلی'), (N'حمل تجهیزات فنی'), (N'اعزام نیرو به پروژه'),
  (N'انتقال مسافر فرودگاهی'), (N'جابه‌مایی دستگاه‌ها'), (N'تأمین مواد اولیه'), (N'بازگشت ضایعات'),
  (N'حمل نمونه آزمایشگاهی'), (N'اعزام تیم پشتیبانی'), (N'توزیع بسته‌های فروش'), (N'انتقال سوخت'),
  (N'حمل قطعات یدکی'), (N'جابه‌مایی تجهیزات اداری'), (N'اعزام نیروی تعمیراتی'), (N'تخلیه بار بندری');

DECLARE @Purposes table (idx int identity(1,1), value nvarchar(500));
INSERT INTO @Purposes (value) VALUES
  (N'به منظور تأمین نیاز انبار'), (N'جهت پشتیبانی از خط تولید'), (N'برای اعزام نیروی فنی'),
  (N'به منظور توزیع محموله'), (N'جهت انتقال تجهیزات'), (N'برای تحویل به پروژه'),
  (N'به منظور جابه‌مایی ملزومات'), (N'جهت خدمات پس از فروش');

DECLARE @Cargo table (idx int identity(1,1), value nvarchar(500));
INSERT INTO @Cargo (value) VALUES
  (N'۱۲ پالت قطعه'), (N'۲۰۰ کارتن بسته‌بندی'), (N'تجهیزات شبکه'), (N'لوله فولادی'),
  (N'سیمان بسته‌ای'), (N'قطعات یدکی'), (N'مواد شیمیایی مجاز'), (N'تجهیزات اندازه‌گیری');

DECLARE @Passengers table (idx int identity(1,1), value nvarchar(1000));
INSERT INTO @Passengers (value) VALUES
  (N'تیم فنی (۴ نفر)'), (N'نیروهای اداری'), (N'پیمانکار'), (N'بازرس'), (N'مدیران منطقه');

DECLARE @Groups table (idx int identity(1,1), value nvarchar(200));
INSERT INTO @Groups (value) VALUES
  (N'گروه ترابری ۱'), (N'گروه ترابری ۲'), (N'گروه پشتیبانی');

DECLARE @SplitReasons table (idx int identity(1,1), value nvarchar(500));
INSERT INTO @SplitReasons (value) VALUES
  (N'خرابی خودرو'), (N'تعویض راننده'), (N'تعویض هم‌زمان راننده و خودرو'), (N'سایر');

DECLARE @FirstCount int  = (SELECT COUNT(*) FROM @FirstNames);
DECLARE @LastCount int   = (SELECT COUNT(*) FROM @LastNames);
DECLARE @BrandCount int  = (SELECT COUNT(*) FROM @Brands);
DECLARE @LetterCount int = (SELECT COUNT(*) FROM @PlateLetters);
DECLARE @CodeCount int   = (SELECT COUNT(*) FROM @IranCodes);
DECLARE @UnitCount int   = (SELECT COUNT(*) FROM @Units);
DECLARE @PlaceCount int  = (SELECT COUNT(*) FROM @Places);
DECLARE @TitleCount int  = (SELECT COUNT(*) FROM @Titles);
DECLARE @PurposeCount int = (SELECT COUNT(*) FROM @Purposes);
DECLARE @CargoCount int  = (SELECT COUNT(*) FROM @Cargo);
DECLARE @PaxCount int    = (SELECT COUNT(*) FROM @Passengers);
DECLARE @GroupCount int  = (SELECT COUNT(*) FROM @Groups);
DECLARE @SplitCount int  = (SELECT COUNT(*) FROM @SplitReasons);

/* ---------- واحدهای سازمانی و مقاصد ---------- */

INSERT INTO OrganizationUnits (Id, Name, IsActive, CreatedAtUtc)
SELECT CAST('D0DE000A-0000-0000-0000-' + RIGHT('000000000000' + CAST(u.idx AS varchar(12)), 12) AS uniqueidentifier),
       u.value, 1, DATEADD(minute, -u.idx * 37, @UtcNow)
FROM @Units u
WHERE NOT EXISTS (SELECT 1 FROM OrganizationUnits e WHERE e.Name = u.value);

INSERT INTO Locations (Id, Name, Status, ProposedBy, ReviewedBy, ReviewedAtUtc, CreatedAtUtc)
SELECT CAST('D0DE000B-0000-0000-0000-' + RIGHT('000000000000' + CAST(p.idx AS varchar(12)), 12) AS uniqueidentifier),
       p.value,
       CASE WHEN p.idx % 7 = 0 THEN 4 WHEN p.idx % 5 = 0 THEN 1 ELSE 2 END,
       @Admin,
       CASE WHEN p.idx % 5 = 0 THEN NULL ELSE @Admin END,
       CASE WHEN p.idx % 5 = 0 THEN NULL ELSE DATEADD(hour, -p.idx * 5, @UtcNow) END,
       DATEADD(hour, -p.idx * 9, @UtcNow)
FROM @Places p
WHERE NOT EXISTS (SELECT 1 FROM Locations e WHERE e.Name = p.value);

/* ---------- خودروها (پیش از رانندگان، به خاطر کلید خارجی) ---------- */

INSERT INTO Vehicles (Id, Brand, Model, PlateFirstTwo, PlateLetter, PlateNextThree, PlateIranCode,
                      ManualStatus, IsArchived, CreatedAtUtc)
SELECT CAST('D0DE0001-0000-0000-0000-' + RIGHT('000000000000' + CAST(v.n AS varchar(12)), 12) AS uniqueidentifier),
       b.brand,
       b.model,
       RIGHT('0' + CAST(10 + (v.n * 7) % 80 AS varchar(2)), 2),
       pl.value,
       RIGHT('00' + CAST((v.n * 13) % 1000 AS varchar(3)), 3),
       ic.value,
       /* خودروهای ۱ تا @DriverCount آماده و اختصاصی راننده هم‌شماره خودند؛ وضعیت تعمیرگاه/خارج از
          سرویس فقط به خودروهای اضافی داده می‌شود تا با مأموریت‌های واقعی در تضاد نباشد */
       CASE WHEN v.n > @DriverCount AND (v.n % 9 = 0 OR v.n % 3 = 0) THEN 2
            WHEN v.n > @DriverCount AND v.n % 11 = 0 THEN 3
            ELSE 1 END,
       CASE WHEN v.n > @DriverCount AND v.n % 4 = 0 THEN 1 ELSE 0 END,
       DATEADD(day, -300 + v.n * 3, @UtcNow)
FROM @Numbers v
CROSS APPLY (SELECT brand, model FROM @Brands WHERE idx = 1 + (v.n - 1) % @BrandCount) b
CROSS APPLY (SELECT value FROM @PlateLetters WHERE idx = 1 + (v.n - 1) % @LetterCount) pl
CROSS APPLY (SELECT value FROM @IranCodes WHERE idx = 1 + (v.n - 1) % @CodeCount) ic
WHERE v.n <= @VehicleCount;

/* ---------- رانندگان ---------- */

INSERT INTO Drivers (Id, FirstName, LastName, MobilePhone, EmploymentStatus, DefaultVehicleId, IsArchived, CreatedAtUtc)
SELECT CAST('D0DE0000-0000-0000-0000-' + RIGHT('000000000000' + CAST(d.n AS varchar(12)), 12) AS uniqueidentifier),
       f.value,
       l.value,
       '09' + RIGHT('0000000000' + CAST(100000000 + d.n * 7919 AS varchar(10)), 9),
       CASE WHEN d.n % 11 = 0 THEN 2 WHEN d.n % 17 = 0 THEN 4 WHEN d.n % 23 = 0 THEN 3 ELSE 1 END,
       /* هر راننده خودروی اختصاصی با همان شماره دارد؛ خط زمانه راننده و خودرو یکی است و
          هیچ‌کدام همپوشانی نمی‌سازند */
       CAST('D0DE0001-0000-0000-0000-' + RIGHT('000000000000' + CAST(d.n AS varchar(12)), 12) AS uniqueidentifier),
       CASE WHEN d.n % 29 = 0 THEN 1 ELSE 0 END,
       DATEADD(day, -260 + d.n, @UtcNow)
FROM @Numbers d
CROSS APPLY (SELECT value FROM @FirstNames WHERE idx = 1 + (d.n - 1) % @FirstCount) f
CROSS APPLY (SELECT value FROM @LastNames WHERE idx = 1 + (d.n - 1) % @LastCount) l
WHERE d.n <= @DriverCount;

/* ---------- کاربران نمونه ---------- */

DECLARE @DemoRoles table (idx int identity(1,1), username nvarchar(100), role nvarchar(100),
                          first nvarchar(100), last nvarchar(100), unit nvarchar(200));
INSERT INTO @DemoRoles (username, role, first, last, unit) VALUES
  (N'demo.operator1',  N'Operator',     N'سمیرا', N'کاظمی',  N'واحد لجستیک'),
  (N'demo.operator2',  N'Operator',     N'پیمان', N'نوروزی', N'واحد توزیع'),
  (N'demo.requester1', N'Requester',    N'مریم',  N'هاشمی',  N'معاونت فنی'),
  (N'demo.requester2', N'Requester',    N'احمد',  N'بهرامی', N'واحد بازرگانی'),
  (N'demo.requester3', N'Requester',    N'الهام', N'زارعی',  N'امور اداری'),
  (N'demo.viewer1',    N'Viewer',       N'فرید',  N'سلطانی', N'واحد پروژه‌ها'),
  (N'demo.manager1',   N'FleetManager', N'حمید',  N'موسوی',  N'واحد لجستیک'),
  (N'demo.manager2',   N'FleetManager', N'نگار',  N'رحیمی',  N'معاونت فنی');

INSERT INTO Users (Id, Username, PasswordHash, FirstName, LastName, MobilePhone, Email, OrganizationalUnit,
                   IsDisabled, IsLocalAdmin, IsDirectoryUser, UpdatedAtUtc, CreatedAtUtc)
SELECT CAST('D0DE0003-0000-0000-0000-' + RIGHT('000000000000' + CAST(u.idx AS varchar(12)), 12) AS uniqueidentifier),
       u.username,
       @PasswordHash,
       u.first,
       u.last,
       '09' + RIGHT('0000000000' + CAST(200000000 + u.idx * 104729 AS varchar(10)), 9),
       NULL,
       u.unit,
       0, 0, 0,
       DATEADD(day, -u.idx, @UtcNow),
       DATEADD(day, -u.idx * 6, @UtcNow)
FROM @DemoRoles u;

INSERT INTO UserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM Users u
JOIN @DemoRoles dr ON dr.username = u.Username
JOIN Roles r ON r.Name = dr.role
WHERE u.Id LIKE 'D0DE0003%';

/* ---------- خط زمانه هر راننده و خودرو (بدون تداخل) ----------

   قاعده سیستم: یک راننده و یک خودرو نمی‌توانند دو مأموریت همپوشان داشته باشند و باید
   حداقل missions.minimum-gap-minutes دقیقه (پیش‌فرض ۳۰) فاصله باشد؛ MissionService همین
   قاعده را در تخصیص، تمدید، اعمال درخواست تغییر و تقسیم با FindConflictAsync می‌سنجد.
   داده نمونه همان قاعده را می‌سازد: برای هر راننده فعال یک خط زمانه راه می‌افتد و
   مأموریت‌ها پشت سر هم، با بیکاری چند ساعته تا چند روزه، روی دو ماه قبل تا دو ماه آینده
   چیده می‌شوند. خودروی اختصاصی هر راننده هم‌شماره اوست، پس خط خودرو هم همپوشانی ندارد.
   صف درخواست‌های بی‌راننده (در انتظار/در حال بررسی) هم پشت سر هم ساخته می‌شود تا ردیف
   «بدون راننده» نیز همپوشانی نشان ندهد. */

DECLARE @Timeline table (
    n          int primary key,
    driverIdx  int NULL,
    vehicleIdx int NULL,
    type       int,
    status     int,
    wallStart  datetime,
    durHours   int);

DECLARE @WindowStart datetime = DATEADD(HOUR, 6,  DATEADD(DAY, -@SpanDays, @TehranDay));
DECLARE @WindowEnd   datetime = DATEADD(HOUR, 22, DATEADD(DAY,  @SpanDays, @TehranDay));
DECLARE @line int, @seq int, @dur int, @idle int, @dayOff int, @rowStatus int;
DECLARE @cursor datetime;
DECLARE @n int = 1;

SET @line = 1;
WHILE @line <= @DriverCount
BEGIN
    /* راننده در مرخصی/تعلیق/قطع همکاری مأموریت نمی‌گیرد؛ همان شرط بخش رانندگان */
    IF CASE WHEN @line % 11 = 0 THEN 2 WHEN @line % 17 = 0 THEN 4 WHEN @line % 23 = 0 THEN 3 ELSE 1 END = 1
    BEGIN
        /* فاز هر خط زمانه متفاوت است تا مأموریت‌ها روی کل بازه پخش شوند */
        SET @cursor = DATEADD(HOUR, 6 + (@line * 5) % 14,
                        DATEADD(DAY, -@SpanDays + (@line * 13) % 45, @TehranDay));
        SET @seq = 1;
        WHILE @cursor < @WindowEnd AND @seq <= @MissionsPerDriver
        BEGIN
            /* مدت از یک ساعت تا چند روز، تا نوارهای کوتاه و کشیده هر دو تست شوند */
            SET @dur = CASE @seq % 5 WHEN 0 THEN 1 + (@line + @seq) % 3
                                     WHEN 1 THEN 4 + (@line * 3 + @seq) % 8
                                     WHEN 2 THEN 12 + (@line + @seq * 5) % 12
                                     WHEN 3 THEN 24 + (@line * 7 + @seq * 3) % 24
                                     ELSE 48 + (@line * 11 + @seq * 7) % 48 END;
            IF DATEADD(HOUR, @dur, @cursor) >= @WindowEnd BREAK;

            /* وضعیت با زمان هم‌خوان: گذشته تکمیل/لغو/رد، امروز در حال اجرا، آینده تأیید/انصراف */
            SET @dayOff = DATEDIFF(day, @TehranDay, CAST(@cursor AS date));
            SET @rowStatus = CASE WHEN @dayOff < 0 THEN CASE (@line + @seq) % 7 WHEN 0 THEN 7 WHEN 3 THEN 8 ELSE 5 END
                                 WHEN @dayOff = 0 THEN CASE (@line + @seq) % 4 WHEN 0 THEN 4 WHEN 3 THEN 5 ELSE 3 END
                                 ELSE CASE (@line + @seq) % 9 WHEN 4 THEN 6 ELSE 3 END END;

            INSERT INTO @Timeline (n, driverIdx, vehicleIdx, type, status, wallStart, durHours)
            VALUES (@n, @line, @line, 1 + @n % 2, @rowStatus, @cursor, @dur);

            SET @n = @n + 1;
            /* فاصله بیکاری میان دو مأموریت همیشه از حداقل فاصله سیستم بیشتر است */
            SET @idle = 40 + (@line * 17 + @seq * 29) % 120;
            SET @cursor = DATEADD(HOUR, @dur + @idle, @cursor);
            SET @seq = @seq + 1;
        END
    END
    SET @line = @line + 1;
END

/* درخواست‌هایی که هنوز راننده و خودرو ندارند؛ فقط آینده، چون تازه ثبت شده‌اند */
SET @cursor = DATEADD(HOUR, 7, @TehranDay);
SET @seq = 1;
WHILE @cursor < @WindowEnd AND @seq <= @RequestCount
BEGIN
    SET @dur = CASE @seq % 4 WHEN 0 THEN 1 + @seq % 3
                             WHEN 1 THEN 5 + (@seq * 5) % 10
                             WHEN 2 THEN 24 + (@seq * 7) % 48
                             ELSE 3 + (@seq * 3) % 6 END;
    IF DATEADD(HOUR, @dur, @cursor) >= @WindowEnd BREAK;

    INSERT INTO @Timeline (n, driverIdx, vehicleIdx, type, status, wallStart, durHours)
    VALUES (@n, NULL, NULL, 1 + @n % 2, CASE @seq % 3 WHEN 0 THEN 2 ELSE 1 END, @cursor, @dur);

    SET @n = @n + 1;
    SET @cursor = DATEADD(HOUR, @dur + 12 + (@seq * 23) % 40, @cursor);
    SET @seq = @seq + 1;
END

/* «امروز» نباید خالی بماند: برای راننده‌های فعال، یک مأموریت در حال اجرا روی همین ساعت
   امروز گذاشته می‌شود، آن‌گاه که با خط زمانه خودشان تداخل نداشته باشد */
DECLARE @runHour int = DATEPART(hour, DATEADD(MINUTE, 210, SYSDATETIMEOFFSET())) - 2;
IF @runHour < 1 SET @runHour = 1;

INSERT INTO @Timeline (n, driverIdx, vehicleIdx, type, status, wallStart, durHours)
SELECT @n + today.rn, today.n, today.n, 1 + today.n % 2, 4,
       DATEADD(HOUR, @runHour, @TehranDay), 3 + today.n % 6
FROM (
    SELECT d.n, ROW_NUMBER() OVER (ORDER BY d.n) AS rn
    FROM @Numbers d
    WHERE d.n <= @DriverCount
      AND CASE WHEN d.n % 11 = 0 THEN 2 WHEN d.n % 17 = 0 THEN 4 WHEN d.n % 23 = 0 THEN 3 ELSE 1 END = 1
) today
WHERE NOT EXISTS (
    SELECT 1 FROM @Timeline x
    WHERE x.driverIdx = today.n
      AND x.wallStart < DATEADD(HOUR, @runHour + 4 + today.n % 6, @TehranDay)
      AND DATEADD(HOUR, x.durHours, x.wallStart) > DATEADD(HOUR, @runHour - 1, @TehranDay));

SET @n = @n + @@ROWCOUNT;

/* ---------- مأموریت‌ها ---------- */

INSERT INTO Missions (Id, MissionNumber, Title, RequesterUsername, RequesterName, RequesterPhoneSnapshot,
                      RequesterOrganizationalUnit, RequestingGroup, Type, Purpose, Origin, Destination, RouteJson,
                      PlannedStartAtUtc, PlannedEndAtUtc, DriverId, VehicleId, Description, PassengerCount,
                      MissionOrderNumber, MissionOrderDate, CargoDescription, ConsigneeName, PassengerNames,
                      Status, ReviewStartedAtUtc, ApprovedBy, ApprovedAtUtc, ActualStartAtUtc, ActualEndAtUtc,
                      OdometerStartKm, OdometerEndKm, DecisionReason, StatusBeforeWithdrawal, RelatedMissionId,
                      SplitReason, SplitAtUtc, SplitBy, UpdatedAtUtc, CreatedAtUtc)
SELECT
    CAST('D0DE0002-0000-0000-0000-' + RIGHT('000000000000' + CAST(b.n AS varchar(12)), 12) AS uniqueidentifier),
    '1405-' + RIGHT('0000' + CAST(@NumberOffset + b.n AS varchar(10)), 4),
    t.value,
    usr.Username,
    usr.FirstName + N' ' + usr.LastName,
    usr.MobilePhone,
    usr.OrganizationalUnit,
    grp.value,
    b.type,
    pp.value,
    org.value,
    dst.value,
    CASE WHEN b.n % 4 = 0 THEN N'["' + stop.value + N'"]' ELSE N'[]' END,
    u.startUtc,
    u.endUtc,
    CASE WHEN b.driverIdx IS NULL THEN NULL
         ELSE CAST('D0DE0000-0000-0000-0000-' + RIGHT('000000000000' + CAST(b.driverIdx AS varchar(12)), 12) AS uniqueidentifier) END,
    CASE WHEN b.vehicleIdx IS NULL THEN NULL
         ELSE CAST('D0DE0001-0000-0000-0000-' + RIGHT('000000000000' + CAST(b.vehicleIdx AS varchar(12)), 12) AS uniqueidentifier) END,
    CASE WHEN b.n % 6 = 0 THEN N'هماهنگی با تحویل‌گیرنده پیش از حرکت الزامی است.' ELSE NULL END,
    CASE WHEN b.type = 2 THEN 1 + b.n % 6 ELSE NULL END,
    CASE WHEN b.status BETWEEN 3 AND 6 THEN 'MO-' + RIGHT('00000' + CAST(b.n AS varchar(10)), 5) ELSE NULL END,
    CASE WHEN b.status NOT BETWEEN 3 AND 6 THEN NULL
         WHEN DATEADD(day, -1, b.wallStart) <= @TehranDay THEN CAST(DATEADD(day, -1, b.wallStart) AS date)
         ELSE CAST(@TehranDay AS date) END,
    CASE WHEN b.type = 1 THEN cg.value ELSE NULL END,
    CASE WHEN b.type = 1 THEN N'تحویل‌گیرنده: ' + dst.value ELSE NULL END,
    CASE WHEN b.type = 2 THEN px.value ELSE NULL END,
    b.status,
    CASE WHEN b.status >= 2 THEN rv.reviewedUtc ELSE NULL END,
    CASE WHEN b.status IN (3, 4, 5, 6) THEN @Admin ELSE NULL END,
    CASE WHEN b.status IN (3, 4, 5, 6) THEN ap.approvedUtc ELSE NULL END,
    CASE WHEN b.status IN (4, 5) THEN u.startUtc ELSE NULL END,
    CASE WHEN b.status = 5 THEN u.endUtc ELSE NULL END,
    /* کیلومتر تابعی خطی از زمان است، پس برای هر خودرو افزایشی و پیوسته می‌ماند */
    CASE WHEN b.status IN (4, 5) THEN 40000 + DATEDIFF(hour, @WindowStart, b.wallStart) * 25 ELSE NULL END,
    CASE WHEN b.status = 5 THEN 40000 + DATEDIFF(hour, @WindowStart, w2.wallEnd) * 25 ELSE NULL END,
    CASE WHEN b.status = 7 THEN N'انصراف درخواست‌کننده پس از تأیید'
         WHEN b.status = 8 THEN N'اولویت مأموریت دیگری بالاتر بود'
         WHEN b.status = 6 THEN N'درخواست انصراف ثبت شد' ELSE NULL END,
    CASE WHEN b.status = 6 THEN CASE WHEN b.n % 2 = 0 THEN 4 ELSE 3 END ELSE NULL END,
    CASE WHEN b.n % 19 = 0 THEN CAST('D0DE0002-0000-0000-0000-' + RIGHT('000000000000' + CAST(b.n - 1 AS varchar(12)), 12) AS uniqueidentifier) ELSE NULL END,
    CASE WHEN b.n % 19 = 0 THEN sr.value ELSE NULL END,
    CASE WHEN b.n % 19 = 0 THEN sp.splitUtc ELSE NULL END,
    CASE WHEN b.n % 19 = 0 THEN @Admin ELSE NULL END,
    up.updatedUtc,
    cr.createdUtc
FROM @Timeline b
/* دامنه فقط ساعت کامل به وقت تهران را می‌پذیرد؛ wallStart و durHours هر دو ساعت کامل هستند */
CROSS APPLY (SELECT DATEADD(HOUR, b.durHours, b.wallStart) AS wallEnd) w2
CROSS APPLY (SELECT TODATETIMEOFFSET(DATEADD(MINUTE, -210, b.wallStart), '+00:00') AS startUtc,
                   TODATETIMEOFFSET(DATEADD(MINUTE, -210, w2.wallEnd), '+00:00') AS endUtc) u
/* هیچ برچسب زمانی نباید به آینده برود؛ مأموریت‌های آینده‌دار تازه ثبت و بررسی شده‌اند */
CROSS APPLY (SELECT CASE WHEN DATEADD(HOUR, -30, u.startUtc) <= @UtcNow THEN DATEADD(HOUR, -30, u.startUtc)
                         ELSE DATEADD(MINUTE, -(120 + b.n % 900), @UtcNow) END AS createdUtc) cr
CROSS APPLY (SELECT CASE WHEN DATEADD(MINUTE, 20, cr.createdUtc) <= @UtcNow THEN DATEADD(MINUTE, 20, cr.createdUtc)
                         ELSE DATEADD(MINUTE, -45, @UtcNow) END AS reviewedUtc) rv
CROSS APPLY (SELECT CASE WHEN DATEADD(MINUTE, 90, cr.createdUtc) <= @UtcNow THEN DATEADD(MINUTE, 90, cr.createdUtc)
                         ELSE DATEADD(MINUTE, -20, @UtcNow) END AS approvedUtc) ap
CROSS APPLY (SELECT CASE WHEN DATEADD(MINUTE, 150, cr.createdUtc) <= @UtcNow THEN DATEADD(MINUTE, 150, cr.createdUtc)
                         ELSE NULL END AS splitUtc) sp
CROSS APPLY (SELECT CASE WHEN DATEADD(HOUR, -2, u.startUtc) <= @UtcNow THEN DATEADD(HOUR, -2, u.startUtc)
                         ELSE DATEADD(MINUTE, -10, @UtcNow) END AS updatedUtc) up
CROSS APPLY (SELECT value FROM @Titles WHERE idx = 1 + (b.n - 1) % @TitleCount) t
CROSS APPLY (SELECT value FROM @Purposes WHERE idx = 1 + (b.n - 1) % @PurposeCount) pp
CROSS APPLY (SELECT value FROM @Places WHERE idx = 1 + (b.n - 1) % @PlaceCount) org
CROSS APPLY (SELECT value FROM @Places WHERE idx = 1 + ((b.n * 5 + 3) % @PlaceCount)) dst
CROSS APPLY (SELECT value FROM @Places WHERE idx = 1 + ((b.n * 7 + 5) % @PlaceCount)) stop
CROSS APPLY (SELECT value FROM @Cargo WHERE idx = 1 + (b.n % @CargoCount)) cg
CROSS APPLY (SELECT value FROM @Passengers WHERE idx = 1 + (b.n % @PaxCount)) px
CROSS APPLY (SELECT value FROM @Groups WHERE idx = 1 + (b.n % @GroupCount)) grp
CROSS APPLY (SELECT value FROM @SplitReasons WHERE idx = 1 + (b.n % @SplitCount)) sr
CROSS APPLY (SELECT u.Username, u.FirstName, u.LastName, u.MobilePhone, u.OrganizationalUnit
             FROM (SELECT Username, FirstName, LastName, MobilePhone, OrganizationalUnit,
                          ROW_NUMBER() OVER (ORDER BY Id) AS rn
                   FROM Users WHERE Id LIKE 'D0DE0003%') u
             WHERE u.rn = 1 + (b.n % 8)) usr
WHERE NOT EXISTS (SELECT 1 FROM Missions existing
                  WHERE existing.MissionNumber = '1405-' + RIGHT('0000' + CAST(@NumberOffset + b.n AS varchar(10)), 4));

/* ---------- درخواست‌های تغییر ---------- */

INSERT INTO MissionChangeRequests (Id, MissionId, RequesterUsername, Reason, ProposedValuesJson, Status,
                                   ReviewerUsername, ReviewStartedAtUtc, ReviewedAtUtc, DecisionReason, CreatedAtUtc)
SELECT CAST('D0DE0004-0000-0000-0000-' + RIGHT('000000000000' + CAST(cr.rn AS varchar(12)), 12) AS uniqueidentifier),
       cr.Id,
       cr.RequesterUsername,
       N'تغییر زمان و مقصد به دلیل تأخیر در بارگیری',
       N'{"Title":"' + cr.Title + N'","Origin":"' + cr.Origin + N'","Destination":"' + nd.value + N'"}',
       CASE cr.rn % 3 WHEN 0 THEN 3 WHEN 1 THEN 1 ELSE 2 END,
       CASE cr.rn % 3 WHEN 1 THEN NULL ELSE @Admin END,
       CASE cr.rn % 3 WHEN 1 THEN NULL ELSE DATEADD(hour, -6, @UtcNow) END,
       CASE cr.rn % 3 WHEN 0 THEN DATEADD(hour, -4, @UtcNow) ELSE NULL END,
       CASE cr.rn % 3 WHEN 0 THEN N'تغییر زمان ثبت و اعمال شد' ELSE NULL END,
       DATEADD(hour, -9, @UtcNow)
FROM (
    SELECT TOP 40
           mi.Id, mi.RequesterUsername, mi.Title, mi.Origin, mi.Destination,
           ROW_NUMBER() OVER (ORDER BY ABS(DATEDIFF(minute, mi.PlannedStartAtUtc, @UtcNow)), mi.Id) AS rn
    FROM Missions mi
    WHERE mi.Id LIKE 'D0DE0002%' AND mi.Status IN (3, 4, 5)
    ORDER BY ABS(DATEDIFF(minute, mi.PlannedStartAtUtc, @UtcNow)), mi.Id
) cr
CROSS APPLY (SELECT value FROM @Places WHERE idx = 1 + (DATALENGTH(cr.Destination) / 2 + cr.rn * 3) % @PlaceCount) nd;

/* ---------- صف پیامک ---------- */

INSERT INTO SmsQueueItems (Id, EventType, EventKey, MissionId, RecipientMobile, RecipientName, MessageBody, Status,
                           AttemptCount, MaxAttempts, NextAttemptAtUtc, ProviderReference, LastError, AcceptedAtUtc,
                           DeliveredAtUtc, LastAttemptAtUtc, DeliveryPollCount, CreatedBy, UpdatedAtUtc, CreatedAtUtc)
SELECT CAST('D0DE0005-0000-0000-0000-' + RIGHT('000000000000' + CAST(sm.rn AS varchar(12)), 12) AS uniqueidentifier),
       sm.EventType,
       CONVERT(varchar(64), sm.MissionId) + '-' + CAST(sm.EventType AS varchar(4)),
       sm.MissionId,
       sm.RecipientMobile,
       sm.RecipientName,
       N'مأموریت ' + sm.MissionNumber + N' ' + sm.BodyText + N' — ناوگان نوین',
       st.Status,
       st.AttemptCount,
       3,
       CASE WHEN st.Status IN (1, 5) THEN DATEADD(minute, 5, @UtcNow) ELSE NULL END,
       CASE WHEN st.Status IN (3, 4) THEN 'ADP-' + RIGHT('000000' + CAST(sm.rn AS varchar(10)), 6) ELSE NULL END,
       CASE WHEN st.Status IN (6, 7) THEN N'خطای سرویس‌دهنده: خطای موقت شبکه' ELSE NULL END,
       CASE WHEN st.Status IN (3, 4) THEN DATEADD(minute, 2, sm.CreatedAtUtc) ELSE NULL END,
       CASE WHEN st.Status = 4 THEN DATEADD(minute, 4, sm.CreatedAtUtc) ELSE NULL END,
       CASE WHEN st.Status IN (1, 5) THEN sm.CreatedAtUtc ELSE DATEADD(minute, 3, sm.CreatedAtUtc) END,
       CASE WHEN st.Status = 3 THEN 1 ELSE 0 END,
       @Admin,
       DATEADD(minute, 4, sm.CreatedAtUtc),
       sm.CreatedAtUtc
FROM (
    SELECT x.MissionId, x.MissionNumber, x.RecipientMobile, x.RecipientName, x.CreatedAtUtc, x.EventType, x.BodyText,
           ROW_NUMBER() OVER (ORDER BY ABS(DATEDIFF(minute, x.CreatedAtUtc, @UtcNow)), x.MissionId) AS rn
    FROM (
        SELECT mi.Id AS MissionId,
               mi.MissionNumber,
               dr.MobilePhone AS RecipientMobile,
               dr.FirstName + N' ' + dr.LastName AS RecipientName,
               mi.PlannedStartAtUtc AS CreatedAtUtc,
               CASE mi.Status WHEN 5 THEN 9 WHEN 7 THEN 5 WHEN 8 THEN 2 WHEN 6 THEN 6
                              WHEN 4 THEN 8 WHEN 3 THEN 1 ELSE 1 END AS EventType,
               CASE mi.Status WHEN 5 THEN N'انجام شد'
                              WHEN 7 THEN N'لغو شد'
                              WHEN 8 THEN N'رد شد'
                              WHEN 6 THEN N'در انتظار تصمیم انصراف است'
                              WHEN 4 THEN N'در حال اجراست'
                              WHEN 3 THEN N'تأیید شد'
                              ELSE N'ثبت شد' END AS BodyText
        FROM Missions mi
        JOIN Drivers dr ON dr.Id = mi.DriverId
        WHERE mi.Id LIKE 'D0DE0002%'
    ) x
) sm
CROSS APPLY (SELECT CASE WHEN sm.rn % 11 = 0 THEN 6
                         WHEN sm.rn % 7 = 0 THEN 5
                         WHEN sm.rn % 5 = 0 THEN 3
                         WHEN sm.rn % 4 = 0 THEN 1
                         ELSE 4 END AS Status,
               CASE WHEN sm.rn % 11 = 0 THEN 3 ELSE 1 END AS AttemptCount) st
WHERE sm.rn <= 120;

/* ---------- شماره‌گذاری مأموریت‌ها ---------- */

UPDATE MissionNumberSequences
SET LastNumber = CASE WHEN LastNumber < @NumberOffset + (SELECT MAX(n) FROM @Timeline)
                      THEN @NumberOffset + (SELECT MAX(n) FROM @Timeline) ELSE LastNumber END
WHERE Year = 1405;

IF NOT EXISTS (SELECT 1 FROM MissionNumberSequences WHERE Year = 1405)
    INSERT INTO MissionNumberSequences (Id, Year, LastNumber, CreatedAtUtc)
    SELECT NEWID(), 1405, @NumberOffset + MAX(n), @UtcNow FROM @Timeline;

/* پنجره ثبت درخواست تا دو ماه آینده باز می‌ماند، وگرنه ویرایش مأموریت‌های در انتظارِ دور از
   تایم‌لاین با خطای «حداکثر تا ۳۰ روز آینده» رد می‌شود. */
IF EXISTS (SELECT 1 FROM SystemSettings WHERE [Key] = N'missions.future-request-days')
    UPDATE SystemSettings SET Value = N'90', UpdatedAtUtc = @UtcNow WHERE [Key] = N'missions.future-request-days';
ELSE
    INSERT INTO SystemSettings (Id, [Key], Value, Description, UpdatedAtUtc, CreatedAtUtc)
    VALUES (NEWID(), N'missions.future-request-days', N'90', N'حداکثر بازه آینده درخواست مأموریت (روز) — داده نمونه', @UtcNow, @UtcNow);

INSERT INTO SystemSettings (Id, [Key], Value, Description, UpdatedAtUtc, CreatedAtUtc)
VALUES (NEWID(), N'seed.demo.version', N'1', N'نشانگر بارگذاری داده نمونه', @UtcNow, @UtcNow);

COMMIT TRANSACTION;

PRINT N'داده نمونه بارگذاری شد.';
SELECT N'مأموریت=' + CAST((SELECT COUNT(*) FROM Missions WHERE Id LIKE 'D0DE0002%') AS varchar(10))
     + N' راننده=' + CAST((SELECT COUNT(*) FROM Drivers WHERE Id LIKE 'D0DE0000%') AS varchar(10))
     + N' خودرو=' + CAST((SELECT COUNT(*) FROM Vehicles WHERE Id LIKE 'D0DE0001%') AS varchar(10))
     + N' کاربر=' + CAST((SELECT COUNT(*) FROM Users WHERE Id LIKE 'D0DE0003%') AS varchar(10))
     + N' پیامک=' + CAST((SELECT COUNT(*) FROM SmsQueueItems WHERE Id LIKE 'D0DE0005%') AS varchar(10))
     + N' درخواست‌تغییر=' + CAST((SELECT COUNT(*) FROM MissionChangeRequests WHERE Id LIKE 'D0DE0004%') AS varchar(10));
