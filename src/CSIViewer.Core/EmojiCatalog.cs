namespace CSIViewer.Core;

public sealed record EmojiCategory(string Key, IReadOnlyList<string> Emojis)
{
    public override string ToString() => Name;
    public string Name => Key switch
    {
        "popular" => Localization.Text("Más usados"),
        "faces" => Localization.Text("Caras y emociones"),
        "people" => Localization.Text("Gestos y personas"),
        "nature" => Localization.Text("Animales y naturaleza"),
        "food" => Localization.Text("Comida y bebida"),
        "travel" => Localization.Text("Viajes y lugares"),
        "activities" => Localization.Text("Actividades"),
        _ => Localization.Text("Objetos y símbolos")
    };
}

/// <summary>Unicode emoji rendered by Windows, not proprietary image assets.</summary>
public static class EmojiCatalog
{
    public static IReadOnlyList<EmojiCategory> Categories { get; } = Array.AsReadOnly(new[]
    {
        Group("popular", "😀 😂 🤣 😊 😍 🥰 😘 😎 🤔 😅 😭 😢 👍 👎 👏 🙏 💪 👋 ❤️ 💙 🎉 🔥 ✅ ❌ 👌 😁 😜 🤗 😱 😴 🙄 😡"),
        Group("faces", "😀 😃 😄 😁 😆 😅 😂 🤣 😊 😇 🙂 🙃 😉 😌 😍 🥰 😘 😗 😙 😚 😋 😛 😝 😜 🤪 🤨 🧐 🤓 😎 🤩 🥳 😏 😒 😞 😔 😟 😕 🙁 ☹️ 😣 😖 😫 😩 🥺 😢 😭 😤 😠 😡 🤬 🤯 😳 🥵 🥶 😱 😨 😰 😥 😓 🤗 🤔 🤭 🤫 🤥 😶 😐 😑 😬 🙄 😯 😦 😧 😮 😲 🥱 😴 🤤 😪 😵 🤐 🥴 🤢 🤮 🤧 😷 🤒 🤕 🤑 🤠 😈 👻 💀 🤖 💩"),
        Group("people", "👋 🤚 🖐️ ✋ 🖖 👌 🤏 ✌️ 🤞 🤟 🤘 🤙 👈 👉 👆 👇 ☝️ 👍 👎 ✊ 👊 🤛 🤜 👏 🙌 👐 🤲 🙏 ✍️ 💪 👀 👂 👃 👶 🧒 👦 👧 🧑 👨 👩 👴 👵 🙋 🙅 🙆 🤷 🤦 🙇 💁 👮 👷 🧑‍💻 🧑‍⚕️ 🧑‍🏫 🧑‍🍳 🧑‍🔧 🧑‍🚀 👑 💃 🕺 👫 👨‍👩‍👧‍👦"),
        Group("nature", "🐶 🐱 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🐔 🐧 🐦 🦆 🦅 🦉 🦇 🐺 🐗 🐴 🦄 🐝 🐛 🦋 🐌 🐞 🐜 🦟 🦗 🕷️ 🦂 🐢 🐍 🦎 🐙 🦑 🦐 🦀 🐠 🐟 🐬 🐳 🦈 🐊 🐘 🦒 🦓 🦍 🦧 🐪 🐕 🐈 🐓 🦚 🦜 🌵 🎄 🌲 🌳 🌴 🌱 🌿 ☘️ 🍀 🍁 🍂 🍃 🌺 🌸 🌼 🌻 🌹 🌷 🌎 🌞 🌝 ⭐ 🌟 ✨ ⚡ 🔥 🌈 ☀️ ⛅ ☁️ 🌧️ ⛈️ ❄️ ☃️ 💧 🌊"),
        Group("food", "🍏 🍎 🍐 🍊 🍋 🍌 🍉 🍇 🍓 🫐 🍈 🍒 🍑 🥭 🍍 🥥 🥝 🍅 🥑 🥦 🥬 🥒 🌶️ 🌽 🥕 🧄 🧅 🥔 🍠 🥐 🍞 🥖 🥨 🧀 🥚 🍳 🥓 🥩 🍗 🍖 🌭 🍔 🍟 🍕 🥪 🌮 🌯 🥙 🧆 🍝 🍜 🍲 🥘 🍣 🍱 🥟 🍤 🍚 🍙 🍦 🍧 🍨 🍩 🍪 🎂 🍰 🧁 🥧 🍫 🍬 🍭 🍯 🍼 🥛 ☕ 🍵 🧃 🥤 🧋 🍺 🍻 🥂 🍷 🍹 🍸 🍾 🍽️"),
        Group("travel", "🚗 🚕 🚙 🚌 🚎 🏎️ 🚓 🚑 🚒 🚐 🚚 🚛 🚜 🛵 🏍️ 🚲 🛴 🚂 🚆 🚇 🚊 🚉 ✈️ 🛫 🛬 🚀 🛸 🚁 ⛵ 🚤 🛳️ ⚓ ⛽ 🚦 🚥 🗺️ 🗿 🗽 🗼 🏰 🏯 🏟️ 🎡 🎢 ⛲ ⛱️ 🏖️ 🏝️ 🏜️ 🌋 🏔️ ⛰️ 🏕️ 🏠 🏡 🏢 🏣 🏥 🏦 🏨 🏫 🏭 ⛪ 🕌 🕍 ⛩️ 🌅 🌄 🌠 🎆 🎇 🌉 🌃 🌆 🌇"),
        Group("activities", "⚽ 🏀 🏈 ⚾ 🥎 🎾 🏐 🏉 🎱 🏓 🏸 🏒 🏑 🥍 🏏 🥅 ⛳ 🏹 🎣 🤿 🥊 🥋 🎽 🛹 🛼 ⛸️ 🥌 🎿 ⛷️ 🏂 🏋️ 🤸 ⛹️ 🤺 🤾 🏌️ 🏇 🧘 🏄 🏊 🚴 🏆 🥇 🥈 🥉 🏅 🎖️ 🎗️ 🎫 🎟️ 🎪 🎭 🎨 🎬 🎤 🎧 🎼 🎹 🥁 🎷 🎺 🎸 🎻 🎲 ♟️ 🎯 🎳 🎮 🕹️ 🧩"),
        Group("objects", "❤️ 🧡 💛 💚 💙 💜 🖤 🤍 🤎 💔 ❣️ 💕 💞 💓 💗 💖 💘 💝 💟 ☮️ ✝️ ☯️ ♾️ ✅ ☑️ ✔️ ❌ ❎ ⚠️ 🚫 ⛔ ❓ ❔ ❗ ❕ ‼️ ⁉️ 💯 🔴 🟠 🟡 🟢 🔵 🟣 ⚫ ⚪ 🟤 🔺 🔻 🔶 🔷 🔸 🔹 🔘 🔲 🔳 ⬛ ⬜ 🕐 ⏰ ⏳ ⌛ 📅 📆 📌 📍 📎 🖇️ 📝 ✏️ 🖊️ 📚 📖 📄 📁 📂 🗂️ 🗃️ 🗑️ 💼 📦 ✉️ 📧 📬 📮 📞 ☎️ 📱 💻 🖥️ ⌨️ 🖱️ 🖨️ 📷 📸 📹 🎥 📺 📻 🔊 🔉 🔈 🔇 🔔 🔕 🔋 🔌 💡 🔦 🕯️ 🔑 🗝️ 🔒 🔓 🔧 🔨 🛠️ ⚙️ 🧰 🧲 🧪 🧬 💊 🩹 💉 🧼 🧽 🧹 🪣 🛏️ 🛋️ 🚪 🪟 🪑 🎁 🎈 🎀 🎊 🎉 💰 💳 💎 🚩 🏳️ 🏴 🏁")
    });

    private static EmojiCategory Group(string key, string values) =>
        new(key, Array.AsReadOnly(values.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
}
