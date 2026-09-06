const mineflayer = require('mineflayer')
const { Vec3 } = require('vec3')
const fs = require('node:fs')
const [portText, expected, resultPath] = process.argv.slice(2)
const port = Number(portText)
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid isolated port')
const bot = mineflayer.createBot({host: '127.0.0.1', port, username: 'HarborProbe', auth: 'offline', version: '1.21.1', viewDistance: 'tiny'})
const timeout = setTimeout(() => finish(new Error('Client verification timeout')), 45000)
let finished = false
let serverReply = false
let chatEcho = false
function finish(error, result) {
  if (finished) return
  finished = true
  clearTimeout(timeout)
  if (error) { console.error(error.stack || error); process.exitCode = 1 }
  else { fs.writeFileSync(resultPath, JSON.stringify(result, null, 2)); console.log('CLIENT_PASS ' + JSON.stringify(result)) }
  bot.quit('CraftHarbor isolated test completed')
  setTimeout(() => process.exit(process.exitCode || 0), 500)
}
bot.on('error', error => finish(error))
bot.on('kicked', reason => finish(new Error('Kicked: ' + JSON.stringify(reason))))
bot.on('messagestr', message => {
  if (message.includes('CraftHarbor_server_reply')) serverReply = true
  if (message.includes('CraftHarbor_live_probe')) chatEcho = true
})
bot.once('spawn', async () => {
  try {
    await bot.waitForChunksToLoad()
    console.log('CLIENT_READY')
    bot.chat('CraftHarbor_live_probe')
    const end = Date.now() + 20000
    let marker
    while (Date.now() < end) {
      marker = bot.blockAt(new Vec3(0, -60, 0))
      if (marker?.name === expected && chatEcho && serverReply) break
      await new Promise(resolve => setTimeout(resolve, 100))
    }
    if (marker?.name !== expected) throw new Error('World marker mismatch: ' + marker?.name + ', expected ' + expected)
    if (!chatEcho || !serverReply) throw new Error('Chat/console roundtrip failed')
    finish(null, {joined: true, chunksReceived: true, marker: marker.name, chatEcho, serverReply, position: bot.entity.position, health: bot.health, protocolVersion: bot.version})
  } catch (error) { finish(error) }
})
