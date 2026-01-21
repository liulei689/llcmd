#Requires AutoHotkey v2.0
SendMode "Input"
SetWorkingDir A_ScriptDir

^q::
{
    presetFile := A_ScriptDir . "\preset.txt"
    preset := "Hello World"
    if FileExist(presetFile)
    {
        try
        {
            content := FileRead(presetFile, "UTF-8")
            ; 去除 UTF-8 BOM
            if (SubStr(content, 1, 3) = Chr(0xEF) Chr(0xBB) Chr(0xBF))
                content := SubStr(content, 4)
            if (content != "")
                preset := content
        }
        catch
        {
            ; ignore
        }
    }
    ; 逐字慢速输入，避免丢字符和自动回车
    Loop StrLen(preset)
    {
        ch := SubStr(preset, A_Index, 1)
        SendInput(ch)
        Sleep 10 ; 可调节，越大越稳（如 30~80ms）
    }
    return
}