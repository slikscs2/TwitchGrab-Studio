"""Progress adapter for the bundled, pinned Streamlink 8.6.2 HLS engine.

Reports completed HLS segments and playlist duration; does not estimate progress
from download wall time. Streamlink still performs downloading and decryption.
"""
import pathlib
import re
import sys
import threading

root = pathlib.Path(__file__).resolve().parent
engine = next((root / "engine").glob("streamlink-*"))
sys.path.insert(0, str(engine / "pkgs"))

from streamlink.stream.hls import hls

lock = threading.RLock()
durations = {}
completed = set()
total_seconds = 0.0
total_count = 0
done_seconds = 0.0


def report():
    print(f"@@PROGRESS|{done_seconds:.3f}|{total_seconds:.3f}|{len(completed)}|{total_count}",
          file=sys.stderr, flush=True)


original_process = hls.HLSStreamWorker.process_segments
original_debug = hls.log.debug


def process_segments(worker, playlist):
    global total_seconds, total_count
    with lock:
        for segment in playlist.segments:
            durations[segment.num] = segment.duration
        if playlist.is_endlist:
            total_seconds = sum(s.duration for s in playlist.segments)
            total_count = len(playlist.segments)
        report()
    return original_process(worker, playlist)


def debug(message, *args, **kwargs):
    global done_seconds
    # The pinned engine emits this only after a successful write/decrypt.
    match = re.fullmatch(r"Segment (\d+) complete", str(message))
    if match:
        number = int(match[1])
        with lock:
            if number in durations and number not in completed:
                completed.add(number)
                done_seconds += durations[number]
                report()
    return original_debug(message, *args, **kwargs)


hls.HLSStreamWorker.process_segments = process_segments
hls.log.debug = debug

if __name__ == "__main__":
    from streamlink_cli.main import main
    main()
