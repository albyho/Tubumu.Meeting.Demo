ffplay -hide_banner -loglevel trace \
-protocol_whitelist "http,https,file,tcp,udp,rtp" \
http://127.0.0.1:9000/api/Recoder/Record.sdp

ffmpeg -hide_banner -loglevel trace \
-protocol_whitelist "http,https,file,tcp,udp,rtp" \
-max_delay 5000 -reorder_queue_size 16384 \
-i http://127.0.0.1:9000/api/Recoder/Record.sdp \
-acodec aac output.aac -y