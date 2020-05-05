<template>
  <div id="app">
    <el-container>
      <el-header>Tubumu Meeting</el-header>
      <el-main>
        <video id="localVideo" :srcObject.prop="localStream" autoplay="autoplay" />
        <video id="remoteVideo" :srcObject.prop="remoteStream" autoplay="autoplay" />
      </el-main>
    </el-container>
  </div>
</template>

<script>
import Logger from "./lib/Logger";
import querystring from "querystring";
import * as mediasoupClient from "mediasoup-client";
import * as signalR from "@microsoft/signalr";

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
      localStream: null,
      remoteStream: null
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

        const host = "https://192.168.18.233:5001";
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

      // 连接成功
      let result = await this.connection.invoke(
        "EnterRoom",
        "00000000-0000-0000-0000-000000000000"
      );
      if (result.code !== 200) {
        logger.error("processMessage() | EnterRoom failure.");
        return;
      }

      // EnterRoom 成功
      result = await this.connection.invoke("GetRouterRtpCapabilities");
      if (result.code !== 200) {
        logger.error("processMessage() | GetRouterRtpCapabilities failure.");
        return;
      }

      // GetRouterRtpCapabilities 成功
      const routerRtpCapabilities = result.data;
      this.mediasoupDevice = new mediasoupClient.Device();
      await this.mediasoupDevice.load({
        routerRtpCapabilities
      });

      result = await this.connection.invoke("CreateWebRtcTransport", {
        forceTcp: false,
        producing: true,
        consuming: false
      });
      if (result.code !== 200) {
        logger.error("processMessage() | CreateWebRtcTransport failure.");
        return;
      }

      // CreateWebRtcTransport 成功
      this.sendTransport = this.mediasoupDevice.createSendTransport({
        id: result.data.id,
        iceParameters: result.data.iceParameters,
        iceCandidates: result.data.iceCandidates,
        dtlsParameters: result.data.dtlsParameters,
        iceServers: [],
        proprietaryConstraints: {
          optional: [{ googDscp: true }]
        }
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
        async ({ kind, rtpParameters, appData }, callback, errback) => {
          logger.debug("sendTransport.on produce");
          try {
            const result = await this.connection.invoke("Produce", {
              transportId: this.sendTransport.id,
              kind,
              rtpParameters,
              appData
            });
            if (result.code !== 200) {
              logger.debug(result.message);
              errback(new Error(result.message));
              return;
            }
            callback({ id: result.data.id });
          } catch (error) {
            errback(error);
          }
        }
      );

      this.sendTransport.on("connectionstatechange", state => {
        logger.debug(`connectionstatechange: ${state}`)
      });

      result = await this.connection.invoke("CreateWebRtcTransport", {
        forceTcp: false,
        producing: false,
        consuming: true
      });

      // CreateWebRtcTransport 成功
      this.recvTransport = this.mediasoupDevice.createRecvTransport({
        id: result.data.id,
        iceParameters: result.data.iceParameters,
        iceCandidates: result.data.iceCandidates,
        dtlsParameters: result.data.dtlsParameters,
        iceServers: []
      });

      this.recvTransport.on(
        "connect",
        ({ dtlsParameters }, callback, errback) => {
          logger.debug("recvTransport.on connect");
          this.connection
            .invoke("ConnectWebRtcTransport", {
              transportId: this.recvTransport.id,
              dtlsParameters
            })
            .then(callback)
            .catch(errback);
        }
      );

      result = await this.connection.invoke("Join", {
        rtpCapabilities: this.mediasoupDevice.rtpCapabilities,
        sctpCapabilities: null // 使用 DataChannel 则取 this.mediasoupDevice.sctpCapabilities
      });
      if (result.code !== 200) {
        logger.error("processMessage() | Join failure.");
        return;
      }

      // Join 成功
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
        //type,
        appData
        //producerPaused
      } = data.data;
      let codecOptions;

      if (kind === "audio") {
        codecOptions = {
          opusStereo: 1
        };
      }

      const consumer = await this.recvTransport.consume({
        id,
        producerId,
        kind,
        rtpParameters,
        codecOptions,
        appData: { ...appData, peerId } // Trick.
      });

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

      // We are ready. Answer the request so the server will
      // resume this Consumer (which was paused for now).
      const result = await this.connection.invoke("NewConsumerReady", {
        peerId: data.data.consumerPeerId,
        consumerId: id
      });

      if (result.code !== 200) {
        logger.error("processNewConsumer() | NewConsumerReady.");
        return;
      }

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

      const stream = new MediaStream();
      stream.addTrack(consumer.track);
      this.remoteStream = stream;
    },
    async processMessage(data) {
      logger.debug("processMessage() | %o", data);
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

        await this._updateAudioDevices();

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
            width: { ideal: 640 },
            aspectRatio: 1.334
          }
        });

        this.localStream = stream;

        track = stream.getVideoTracks()[0];

        this.webcamProducer = await this.sendTransport.produce({
          track,
          appData: {
            source: "webcam"
          }
        });

        await this._updateWebcams();

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
  width:320px;
}

</style>
