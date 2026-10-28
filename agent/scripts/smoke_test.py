# pip install websockets
import asyncio
import json
import websockets


async def main():
    async with websockets.connect("ws://localhost:8765") as ws:
        async def call(**req):
            await ws.send(json.dumps(req))
            return json.loads(await ws.recv())

        r = await call(id=1, cmd="reset", seed=1, mode="visible_only")
        print("\n".join(r["observation"]["map"]))

        for i, d in enumerate(["up", "up", "right", "right", "right"], start=2):
            r = await call(id=i, cmd="move", direction=d)
            print(f"{d:>5} -> ok={r['ok']} success={r['success']} event={r['event']}")

        print("\n".join(r["observation"]["map"]))


asyncio.run(main())