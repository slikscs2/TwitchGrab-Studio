# Внешние компоненты

TwitchGrab Studio запускает самостоятельные программы Streamlink и FFmpeg.
Они распространяются со своими лицензиями. Их лицензии и лицензии вложенных
зависимостей сохраняются в каталоге `engine` установщика и portable-архива.

| Компонент | Источник |
| --- | --- |
| Streamlink Windows 8.6.2-2, Python 3.14 | https://github.com/streamlink/windows-builds/releases/tag/8.6.2-2 |
| Исходники Streamlink | https://github.com/streamlink/streamlink/tree/8.6.2 |
| Рецепты сборки Windows и FFmpeg | https://github.com/streamlink/windows-builds/tree/8.6.2-2 |
| Исходники FFmpeg | https://ffmpeg.org/download.html |
| Исходники Python | https://www.python.org/downloads/source/ |

Исходный архив движка:
`streamlink-8.6.2-2-py314-x86_64.zip`

SHA256:
`12e0c8e43b736f75ec67e44cd375ee05be301d7f25a5e2ec1aaa1f81c67ff2b3`

FFmpeg из этого архива сопровождается GPLv3; полный текст находится в
`engine/streamlink-8.6.2-2-py314-x86_64/ffmpeg/LICENSE.txt`.
Условия Streamlink, Python и их зависимостей находятся рядом с компонентами.
Шрифты не включены в релиз: при обработке используется установленный в Windows шрифт.

Название и ссылки Twitch/CapCut приводятся для описания совместимости и ориентира;
проект не связан с их владельцами. Логотип и оформление TwitchGrab предоставлены автором проекта.
