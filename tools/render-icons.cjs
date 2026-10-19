// 构建资源时使用已有 sharp；生成文件已提交，正常 dotnet 构建不依赖 Node.js。
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require(process.env.LLAMA_ICON_SHARP || 'sharp');
const project = path.resolve(__dirname, '..');
const assets = path.join(project, 'src', 'Assets');

async function main() {
  const svg = await fs.readFile(path.join(assets, 'app.svg'));
  await sharp(svg, { density: 384 }).resize(512, 512).png().toFile(path.join(assets, 'app-512.png'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(svg, { density: 384 }).resize(size, size).png().toBuffer()));
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(1, 2); directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  sizes.forEach((size, i) => {
    const start = 6 + 16 * i;
    directory[start] = size === 256 ? 0 : size;
    directory[start + 1] = size === 256 ? 0 : size;
    directory.writeUInt16LE(1, start + 4); directory.writeUInt16LE(32, start + 6);
    directory.writeUInt32LE(frames[i].length, start + 8); directory.writeUInt32LE(offset, start + 12);
    offset += frames[i].length;
  });
  await fs.writeFile(path.join(assets, 'app.ico'), Buffer.concat([directory, ...frames]));
  const canvas = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1080" height="520"><rect width="1080" height="520" rx="28" fill="#101827"/><text x="48" y="58" font-family="Segoe UI,Arial" font-size="26" fill="#ECF4FF">Llama Launcher</text><text x="48" y="91" font-family="Segoe UI,Arial" font-size="15" fill="#93A8C5">App icon + model icons / vector assets</text></svg>`);
  const composites = [{ input: await sharp(svg, { density: 192 }).resize(280, 280).png().toBuffer(), left: 48, top: 133 }];
  const entries = [['qwen-color.svg', 'Qwen', '#F1EDFF'], ['openai.svg', 'GPT-OSS', '#E8F3ED'], ['hunyuan-color.svg', 'HY-MT2', '#EAF4FF'], ['gguf.svg', 'GGUF', '#E8F0FA']];
  for (let i = 0; i < entries.length; i++) {
    const [file, name, color] = entries[i];
    const x = 408 + (i % 2) * 305, y = 142 + Math.floor(i / 2) * 164;
    const background = Buffer.from(`<svg width="268" height="132" xmlns="http://www.w3.org/2000/svg"><rect width="268" height="132" rx="22" fill="#1D293C"/><rect x="20" y="24" width="84" height="84" rx="20" fill="${color}"/><text x="126" y="76" font-family="Segoe UI,Arial" font-size="22" fill="#EEF5FF">${name}</text></svg>`);
    composites.push({ input: background, left: x, top: y });
    composites.push({ input: await sharp(path.join(assets, file), { density: 192 }).resize(60, 60).png().toBuffer(), left: x + 32, top: y + 36 });
  }
  await sharp(canvas).composite(composites).png().toFile(path.join(project, '图标预览-1.2.1.png'));
  console.log(`Generated app.ico (${sizes.length} sizes), PNG and icon preview.`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
