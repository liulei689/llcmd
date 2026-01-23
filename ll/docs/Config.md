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
  "StartMinimized": true,
  "PresetText": "Hello World",
  "TotalRuntimeSeconds": 0
}
```

## 更新配置
编辑 config.json；重启程序。

## 压缩文件夹
指令：`zip <源路径> [输出ZIP路径] [密码]` 或 `zip <源路径> [密码] [输出ZIP路径]`
描述：压缩指定文件或文件夹到ZIP文件，可选密码保护。如果不指定输出路径，默认在源路径父目录生成同名ZIP文件（自动添加数字避免冲突）。
示例：
- `zip C:\MyFolder` - 压缩到 C:\MyFolder.zip
- `zip C:\MyFolder output.zip` - 压缩到 output.zip
- `zip C:\MyFolder mypassword` - 压缩到 C:\MyFolder.zip 并加密
- `zip C:\MyFolder output.zip mypassword` - 指定路径和密码

## 解压ZIP文件
指令：`unzip <ZIP文件> <输出目录> [密码]`
描述：解压ZIP文件到指定目录，可选密码。
示例：
- `unzip output.zip C:\Extracted` - 无密码解压
- `unzip output.zip C:\Extracted mypassword` - 带密码解压