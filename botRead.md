# .botRule — Quy tắc cho AI coding agent trong project ass2

- Dự án: ASP.NET Core 8 (Razor Pages), không phải Blazor/React cho phần UI trong Pages/*.cshtml.
  (Riêng Components/*.razor là Blazor Server component nhúng qua <component type="typeof(...)" />, chỉ sửa khi được yêu cầu rõ.)
- Không đổi tên asp-page-handler, PageModel property, hoặc field name form (Title, Subject, Chapter, UploadFile, ManualContent, DocumentId...) trừ khi được yêu cầu.
- Backend chỉ dùng C#, không dùng Java hay bất kỳ ngôn ngữ backend nào khác.
- Client-side chỉ dùng JS thuần (vanilla), không thêm npm/bundler, không thêm thư viện ngoài (React, jQuery, TypeScript...) nếu không được yêu cầu.
- Style/CSS đặt tại wwwroot/css/site.css, không tạo framework CSS mới (không thêm Tailwind/Bootstrap component lạ).
- Giữ nguyên logic C# trong *.cshtml.cs trừ khi yêu cầu rõ ràng đổi logic.
- Text hiển thị dùng tiếng Việt có dấu, kể cả khi code hiện tại đang viết không dấu (VD: sửa "Sua thong tin tai lieu" thành "Sửa thông tin tài liệu" nếu đụng tới đoạn đó), trừ khi được yêu cầu giữ nguyên không dấu.
