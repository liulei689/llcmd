# docs/Config.md - LL CLI TOOL

## 配置结构
```json
{
  "Database": {
    "Username": "postgres",
    "Password": "xxx",
    "Database": "xxx"
  },
  "SSH": {
    "Enabled": true,
    "Host": "host",
    "Port": 22,
    "Username": "xxx",
    "Password": "xxx"
  },
  "Email": {
    "SmtpHost": "smtp.qq.com",
    "SmtpPort": 587,
    "Username": "user@qq.com",
    "Password": "xxx"
  },
  "TotalRuntimeSeconds": 0
}
```

## 更新配置
编辑 config.json；重启程序。