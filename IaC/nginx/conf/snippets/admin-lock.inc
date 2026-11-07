# Замок по адресу на весь сайт — и на статику, и на /api/, до всякой авторизации приложения
# (спец. admin, 7). Маска подхватывает два файла, оба лежат на сервере, а не в репозитории:
# постоянный список `allow ...;` в conf/admin-allow.inc (на MVP не заводится) и
# conf/admin-allow-open.inc с `allow all;`, который на время пишет и стирает workflow
# Admin Access (IaC/DEPLOYMENT.md, «Админка»). Нет ни одного — не открывается никому:
# include с маской на пустом месте не ошибка, а `deny all` остаётся
include /etc/nginx/conf.d/admin-allow*.inc;
deny all;
