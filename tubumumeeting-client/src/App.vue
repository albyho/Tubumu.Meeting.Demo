<template>
  <div id="app">
    <el-container>
      <el-header>Tubumu Meeting</el-header>
      <el-main>
        <video id="localVideo" :srcObject.prop="localVideoStream" autoplay="autoplay" />
        <video id="remoteVideo" :srcObject.prop="remoteVideoStream" autoplay="autoplay" />
        <audio
          id="remoteAudio"
          :srcObject.prop="remoteAudioStream"
          autoplay="autoplay"
        />
      </el-main>
    </el-container>
  </div>
</template>

<script>
import Logger from "./lib/Logger";
import querystring from "querystring";
import * as mediasoupClient from "mediasoup-client";
import * as signalR from "@microsoft/signalr";

const VIDEO_CONSTRAINS = {
  qvga: { width: { ideal: 320 }, height: { ideal: 240 } },
  vga: { width: { ideal: 640 }, height: { ideal: 480 } },
  hd: { width: { ideal: 1280 }, height: { ideal: 720 } }
};

const PC_PROPRIETARY_CONSTRAINTS = {
  optional: [{ googDscp: true }]
};

const WEBCAM_SIMULCAST_ENCODINGS = [
  { scaleResolutionDownBy: 4 },
  { scaleResolutionDownBy: 2 },
  { scaleResolutionDownBy: 1 }
];

// Used for VP9 webcam video.
const WEBCAM_KSVC_ENCODINGS = [
  { scalabilityMode: "S3T3_KEY" }
];

// Used for simulcast screen sharing.
// eslint-disable-next-line no-unused-vars
const SCREEN_SHARING_SIMULCAST_ENCODINGS =
[
	{ dtx: true, maxBitrate: 1500000 },
	{ dtx: true, maxBitrate: 6000000 }
];

// Used for VP9 screen sharing.
// eslint-disable-next-line no-unused-vars
const SCREEN_SHARING_SVC_ENCODINGS =
[
	{ scalabilityMode: 'S3T3', dtx: true }
];

const logger = new Logger("App");

localStorage.setItem("debug", "mediasoup-client:* tubumumeeting-client:*");

export default {
  name: "app",
  components: {},
  data() {
    return {
      connection: null,
      mediasoupDevice: null,
      sendTransport: null,
      recvTransport: null,
      consumers: new Map(),
      webcams: {},
      audioDevices: {},
      webcamProducer: null,
      micProducer: null,
      useSimulcast: true,
      forceH264: false,
      forceVP9: false,
      localVideoStream: null,
      remoteVideoStream: null,
      remoteAudioStream: null
    };
  },
  mounted() {
    this.run();
  },
  methods: {
    run() {
      try {
        const { peerId } = querystring.parse(location.search.replace("?", ""));
        const accessTokens = [
          "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMjkiLCJnIjoi5Yy76ZmiIiwibmJmIjoxNTg0MzQ5MDQ2LCJleHAiOjE1ODY5NDEwNDYsImlzcyI6Imlzc3VlciIsImF1ZCI6ImF1ZGllbmNlIn0._bGG1SOF9WqY8TIErRkxsh9_l_mFB_5JcGrKO1GyQ0E",
          "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMTg3IiwiZyI6IuWMu-mZoiIsIm5iZiI6MTU4NzcxNzU2NSwiZXhwIjoxNTkwMzA5NTY1LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.qjvvJB8EHaerbeKnrmfnN3BJ5jh4R_pG99oS1I7ZAvw"
        ];

        const host = "https://192.168.0.124:5001";
        this.connection = new signalR.HubConnectionBuilder()
          .withUrl(
            `${host}/hubs/meetingHub?access_token=${accessTokens[peerId]}`
          )
          // .withAutomaticReconnect({
          //   nextRetryDelayInMilliseconds: retryContext => {
          //     if (retryContext.elapsedMilliseconds < 60000) {
          //       // If we've been reconnecting for less than 60 seconds so far,
          //       // wait between 0 and 10 seconds before the next reconnect attempt.
          //       return Math.random() * 10000;
          //     } else {
          //       // If we've been reconnecting for more than 60 seconds so far, stop reconnecting.
          //       return null;
          //     }
          //   }
          // })
          .build();

        this.connection.on("PeerHandled", async data => {
          await this.processPeerHandled(data);
        });
        this.connection.on("NewConsumer", async data => {
          await this.processNewConsumer(data);
        });
        this.connection.on("ReceiveMessage", async data => {
          await this.processMessage(data);
        });
        this.connection.start().catch(err => logger.error(err));
      } catch (e) {
        logger.debug(e.message);
      }
    },
    async processPeerHandled(data) {
      if (data.code !== 200) {
        logger.error(data.message);
        return;
      }

      // 连接成功, GetRouterRtpCapabilities
      let result = await this.connection.invoke("GetRouterRtpCapabilities");
      if (result.code !== 200) {
        logger.error("processMessage() | GetRouterRtpCapabilities failure.");
        return;
      }

      const routerRtpCapabilities = result.data;
      this.mediasoupDevice = new mediasoupClient.Device();
      await this.mediasoupDevice.load({
        routerRtpCapabilities
      });

      // GetRouterRtpCapabilities 成功, Join
      result = await this.connection.invoke("Join", {
        rtpCapabilities: this.mediasoupDevice.rtpCapabilities,
        sctpCapabilities: null, // 使用 DataChannel 则取 this.mediasoupDevice.sctpCapabilities
        sources: ['mic', 'webcam'],
        groupId: "00000000-0000-0000-0000-000000000000",
        appData: {}
      });
      if (result.code !== 200) {
        logger.error("processMessage() | Join failure.");
        return;
      }

      // Join成功，CreateWebRtcTransport(生产) 
      result = await this.connection.invoke("CreateWebRtcTransport", {
        forceTcp: false,
        producing: true,
        consuming: false,
        sctpCapabilities: null // 使用 DataChannel 则取 this.mediasoupDevice.sctpCapabilities
      });
      if (result.code !== 200) {
        logger.error("processMessage() | CreateWebRtcTransport failure.");
        return;
      }

      // CreateWebRtcTransport(生产), createSendTransport(生产)
      this.sendTransport = this.mediasoupDevice.createSendTransport({
        id: result.data.id,
        iceParameters: result.data.iceParameters,
        iceCandidates: result.data.iceCandidates,
        dtlsParameters: result.data.dtlsParameters,
        sctpParameters: result.data.sctpParameters,
        iceServers: [],
        proprietaryConstraints: PC_PROPRIETARY_CONSTRAINTS
      });

      this.sendTransport.on(
        "connect",
        ({ dtlsParameters }, callback, errback) => {
          logger.debug("sendTransport.on connect dtls: %o", dtlsParameters);
          this.connection
            .invoke("ConnectWebRtcTransport", {
              transportId: this.sendTransport.id,
              dtlsParameters
            })
            .then(callback)
            .catch(errback);
        }
      );

      this.sendTransport.on(
        "produce",
        // eslint-disable-next-line no-unused-vars
        async ({ kind, rtpParameters, appData }, callback, errback) => {
          logger.debug("sendTransport.on produce");
          try {
            const result = await this.connection.invoke("Produce", {
              transportId: this.sendTransport.id,
              kind,
              rtpParameters,
              source: appData.source,
              appData
            });
            if (result.code !== 200) {
              logger.debug(result.message);
              errback(new Error(result.message));
              return;
            }
            callback({ id: result.data.id });

            await this.connection.invoke("Consume", {
              peerId: 29,
              sources: [ appData.source ]
            });

          } catch (error) {
            errback(error);
          }
        }
      );

      this.sendTransport.on("connectionstatechange", state => {
        logger.debug(`connectionstatechange: ${state}`);
      });

      // createSendTransport 成功, CreateWebRtcTransport(消费)
      result = await this.connection.invoke("CreateWebRtcTransport", {
        forceTcp: false,
        producing: false,
        consuming: true,
        sctpCapabilities: null // 使用 DataChannel 则取 this.mediasoupDevice.sctpCapabilities
      });

      // CreateWebRtcTransport(消费), createRecvTransport
      this.recvTransport = this.mediasoupDevice.createRecvTransport({
        id: result.data.id,
        iceParameters: result.data.iceParameters,
        iceCandidates: result.data.iceCandidates,
        dtlsParameters: result.data.dtlsParameters,
        sctpParameters: result.data.sctpParameters,
        iceServers: []
      });

      this.recvTransport.on(
        "connect",
        ({ dtlsParameters }, callback, errback) => {
          logger.debug("recvTransport.on connect dtls: %o", dtlsParameters);
          this.connection
            .invoke("ConnectWebRtcTransport", {
              transportId: this.recvTransport.id,
              dtlsParameters
            })
            .then(callback)
            .catch(errback);
        }
      );

      if (this.mediasoupDevice.canProduce("audio")) {
        this.enableMic();
      }

      if (this.mediasoupDevice.canProduce("video")) {
        this.enableWebcam();
      }
    },
    async processNewConsumer(data) {
      const {
        peerId,
        producerId,
        id,
        kind,
        rtpParameters,
        //type, // mediasoup-client 的 Transport.ts 不使用该参数
        appData
        //producerPaused // mediasoup-client 的 Transport.ts 不使用该参数
      } = data.data;

      const consumer = await this.recvTransport.consume({
        id,
        producerId,
        kind,
        rtpParameters,
        appData: { ...appData, peerId } // Trick.
      });

      // Store in the map.
      this.consumers.set(consumer.id, consumer);

      consumer.on("transportclose", () => {
        this.consumers.delete(consumer.id);
      });

      const {
        // eslint-disable-next-line no-unused-vars
        spatialLayers,
        // eslint-disable-next-line no-unused-vars
        temporalLayers
      } = mediasoupClient.parseScalabilityMode(
        consumer.rtpParameters.encodings[0].scalabilityMode
      );

      /*
      if (kind === "audio") {
        consumer.volume = 0;

        const stream = new MediaStream();

        stream.addTrack(consumer.track);

        if (!stream.getAudioTracks()[0]) {
          throw new Error(
            "request.newConsumer | given stream has no audio track"
          );
        }
      }
      */

      const stream = new MediaStream();
      stream.addTrack(consumer.track);

      if (kind === "video") {
        this.remoteVideoStream = stream;
      } else {
        this.remoteAudioStream = stream;
      }

      // We are ready. Answer the request so the server will
      // resume this Consumer (which was paused for now).
      const result = await this.connection.invoke("NewConsumerReturn", {
        peerId: data.data.consumerPeerId,
        consumerId: id
      });

      if (result.code !== 200) {
        logger.error("processNewConsumer() | NewConsumerReturn.");
        return;
      }
    },
    async processMessage(data) {
      logger.debug("processMessage() | %o", data);
      switch (data.internalCode) {
        case "producerScore": {
          // eslint-disable-next-line no-unused-vars
          const { producerId, score } = data.data;

          break;
        }

        case "newPeer": {
          // eslint-disable-next-line no-unused-vars
          const peer = data.data;

          break;
        }

				case 'peerClosed':
				{
          // eslint-disable-next-line no-unused-vars
					const { peerId } = data.data;

					break;
        }
        
        case 'downlinkBwe':
				{
					logger.debug('\'downlinkBwe\' event:%o', data.data);

					break;
				}

        case "consumerClosed": {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          consumer.close();
          this.consumers.delete(consumerId);

          break;
        }

        case "consumerPaused": {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case "consumerResumed": {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case "consumerLayersChanged": {
          // eslint-disable-next-line no-unused-vars
          const { consumerId, spatialLayer, temporalLayer } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case "consumerScore": {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        default: {
          logger.error('unknown protoo data.method "%s"', data.internalCode);
        }
      }
    },
    async enableMic() {
      logger.debug("enableMic()");

      if (this.micProducer) return;
      if (this.mediasoupDevice && !this.mediasoupDevice.canProduce("audio")) {
        logger.error("enableMic() | cannot produce audio");
        return;
      }

      let track;

      try {
        const deviceId = await this._getAudioDeviceId();

        const device = this.audioDevices[deviceId];

        if (!device) throw new Error("no audio devices");

        logger.debug(
          "enableMic() | new selected audio device [device:%o]",
          device
        );

        logger.debug("enableMic() | calling getUserMedia()");

        const stream = await navigator.mediaDevices.getUserMedia({
          audio: {
            deviceId: { ideal: deviceId }
          }
        });

        track = stream.getAudioTracks()[0];

        this.micProducer = await this.sendTransport.produce({
          track,
          codecOptions: {
            opusStereo: 1,
            opusDtx: 1
          },
          appData: { source: "mic" }
        });

        this.micProducer.on("transportclose", () => {
          this.micProducer = null;
        });

        this.micProducer.on("trackended", () => {
          this.disableMic().catch(() => {});
        });

        this.micProducer.volume = 0;
      } catch (error) {
        console.log("enableMic() failed:%o", error);
        logger.error("enableMic() failed:%o", error);
        if (track) track.stop();
      }
    },
    async disableMic() {
      logger.debug("disableMic()");
      if (!this.micProducer) return;

      this.micProducer.close();

      try {
        await this.connection.invoke("closeProducer", {
          producerId: this.micProducer.id
        });
      } catch (error) {
        logger.error('disableMic() [error:"%o"]', error);
      }

      this.micProducer = null;
    },
    async enableWebcam() {
      logger.debug("enableWebcam()");

      if (this.webcamProducer) return;
      if (this.mediasoupDevice && !this.mediasoupDevice.canProduce("video")) {
        logger.error("enableWebcam() | cannot produce video");

        return;
      }

      let track;

      try {
        const deviceId = await this._getWebcamDeviceId();

        const device = this.webcams[deviceId];

        if (!device) throw new Error("no webcam devices");

        logger.debug(
          "_setWebcamProducer() | new selected webcam [device:%o]",
          device
        );

        logger.debug("_setWebcamProducer() | calling getUserMedia()");

        const stream = await navigator.mediaDevices.getUserMedia({
          video: {
            deviceId: { ideal: deviceId },
            ...VIDEO_CONSTRAINS.hd
          }
        });

        this.localVideoStream = stream;

        track = stream.getVideoTracks()[0];

        let encodings;
        let codec;
        const codecOptions =
        {
          videoGoogleStartBitrate : 1000
        };
      
        if (this.forceH264)
        {
          codec = this.mediasoupDevice.rtpCapabilities.codecs
            .find((c) => c.mimeType.toLowerCase() === 'video/h264');

          if (!codec)
          {
            throw new Error('desired H264 codec+configuration is not supported');
          }
        }
        else if (this.forceVP9)
        {
          codec = this.mediasoupDevice.rtpCapabilities.codecs
            .find((c) => c.mimeType.toLowerCase() === 'video/vp9');

          if (!codec)
          {
            throw new Error('desired VP9 codec+configuration is not supported');
          }
        }
      
        if (this.useSimulcast) {
          // If VP9 is the only available video codec then use SVC.
          const firstVideoCodec = this.mediasoupDevice.rtpCapabilities.codecs.find(
            c => c.kind === "video"
          );

          if (firstVideoCodec.mimeType.toLowerCase() === "video/vp9")
            encodings = WEBCAM_KSVC_ENCODINGS;
          else encodings = WEBCAM_SIMULCAST_ENCODINGS;
        }

        this.webcamProducer = await this.sendTransport.produce({
          track,
          encodings,
          codecOptions,
          codec,
          appData: {
            source: "webcam"
          }
        });

        this.webcamProducer.on("transportclose", () => {
          this.webcamProducer = null;
        });

        this.webcamProducer.on("trackended", () => {
          this.disableWebcam().catch(() => {});
        });

        logger.debug("_setWebcamProducer() succeeded");
      } catch (error) {
        logger.error("_setWebcamProducer() failed:%o", error);

        if (track) track.stop();
      }
    },
    async disableWebcam() {
      logger.debug("disableWebcam()");

      if (!this.webcamProducer) return;

      this.webcamProducer.close();

      try {
        await this.connection.invoke("closeProducer", {
          producerId: this.webcamProducer.id
        });
      } catch (error) {
        logger.error('disableWebcam() [error:"%o"]', error);
      }

      this.webcamProducer = null;
    },
    async _updateAudioDevices() {
      logger.debug("_updateAudioDevices()");

      // Reset the list.
      this.audioDevices = {};

      try {
        logger.debug("_updateAudioDevices() | calling enumerateDevices()");

        const devices = await navigator.mediaDevices.enumerateDevices();

        for (const device of devices) {
          if (device.kind !== "audioinput") continue;

          this.audioDevices[device.deviceId] = device;
        }
      } catch (error) {
        logger.error("_updateAudioDevices() failed:%o", error);
      }
    },
    async _updateWebcams() {
      logger.debug("_updateWebcams()");

      // Reset the list.
      this.webcams = {};

      try {
        logger.debug("_updateWebcams() | calling enumerateDevices()");

        const devices = await navigator.mediaDevices.enumerateDevices();

        for (const device of devices) {
          if (device.kind !== "videoinput") continue;

          this.webcams[device.deviceId] = device;
        }
      } catch (error) {
        logger.error("_updateWebcams() failed:%o", error);
      }
    },
    async _getAudioDeviceId() {
      logger.debug("_getAudioDeviceId()");

      try {
        logger.debug("_getAudioDeviceId() | calling _updateAudioDeviceId()");

        await this._updateAudioDevices();

        const audioDevices = Object.values(this.audioDevices);
        return audioDevices[0] ? audioDevices[0].deviceId : null;
      } catch (error) {
        logger.error("_getAudioDeviceId() failed:%o", error);
      }
    },
    async _getWebcamDeviceId() {
      logger.debug("_getWebcamDeviceId()");

      try {
        logger.debug("_getWebcamDeviceId() | calling _updateWebcams()");

        await this._updateWebcams();

        const webcams = Object.values(this.webcams);
        return webcams[0] ? webcams[0].deviceId : null;
      } catch (error) {
        logger.error("_getWebcamDeviceId() failed:%o", error);
      }
    }
  }
};
</script>

<style>
body {
  margin: 0;
  background-color: #313131;
  color: #fff;
}

#app {
  font-family: "Avenir", Helvetica, Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

#app > .el-container {
  margin-bottom: 40px;
}

.el-header,
.el-footer {
  line-height: 60px;
}

.el-main {
  line-height: 160px;
}

video {
  width: 640px;
}

video#localVideo {
    transform: rotateY(180deg);   /* 水平镜像翻转 */
}
</style>
