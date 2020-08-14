<template>
  <div id="app">
    <el-container>
      <el-header>Meeting</el-header>
      <el-main>
        <video id="localVideo" ref="localVideo" v-if="produce" :srcObject.prop="localVideoStream" autoplay playsinline />
        <video v-for="(value, key) in remoteVideoStreams" :key="key" :srcObject.prop="value" autoplay playsinline />
        <audio v-for="(value, key) in remoteAudioStreams" :key="key" :srcObject.prop="value" autoplay />
      </el-main>
    </el-container>
  </div>
</template>

<script>
import Logger from './lib/Logger';
import querystring from 'querystring';
import * as mediasoupClient from 'mediasoup-client';
import * as signalR from '@microsoft/signalr';

// eslint-disable-next-line no-unused-vars
const VIDEO_CONSTRAINS = {
  qvga: { width: { ideal: 320 }, height: { ideal: 240 } },
  vga: { width: { ideal: 640 }, height: { ideal: 480 } },
  hd: { width: { ideal: 1280 }, height: { ideal: 720 } }
};

const PC_PROPRIETARY_CONSTRAINTS = {
  optional: [{ googDscp: true }]
};

// eslint-disable-next-line no-unused-vars
const WEBCAM_SIMULCAST_ENCODINGS = [
  { scaleResolutionDownBy: 4, maxBitrate: 500000 },
  { scaleResolutionDownBy: 2, maxBitrate: 1000000 },
  { scaleResolutionDownBy: 1, maxBitrate: 5000000 }
];

// Used for VP9 webcam video.
// eslint-disable-next-line no-unused-vars
const WEBCAM_KSVC_ENCODINGS = [
  { scalabilityMode: 'S3T3_KEY' }
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

const logger = new Logger('App');

// 'mediasoup-client:* tubumumeeting-client:*'
localStorage.setItem('debug', 'tubumumeeting-client:*');

export default {
  name: 'app',
  components: {},
  data() {
    return {
      peerId: null,
      connection: null,
      mediasoupDevice: null,
      sendTransport: null,
      recvTransport: null,
      consume: true,
      produce: false,
      webcams: {},
      audioDevices: {},
      webcamProducer: null,
      micProducer: null,
      useSimulcast: false,
      forceH264: false,
      forceVP9: false,
      localVideoStream: null,
      remoteVideoStreams: {},
      remoteAudioStreams: {},
      rooms: new Map(),
      peers: new Map(),
      consumers: new Map()
    };
  },
  async mounted() {
    await this.run();
  },
  methods: {
    async run() {
      try {
        const { peerId, peerid } = querystring.parse(location.search.replace('?', ''));
        this.peerId = peerId || peerid;
        this.produce = this.peerId !== '0' && this.peerId !== '1';
        const accessTokens = [
          'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.5w00ixg06pRPxcdbtbmRVI6Wy_Ta9qsSJc3D7PE3chQ',
          'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMiIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.q2tKWUa6i4u0VpZDhA8Fw92NoV_g9YQeWD-OeF7fAvU',
          'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMyIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.ekjd10Bortc1q34Ani1F_Gw9KQwS4qRFIU715pE1lGo',
          'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiNCIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.6hZ4GtamurlemceCV7I5vT-UQkbszRxys5h8QOiOhcE',
          'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiNSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.nV4fr0tYGDR7zsykNoFYERdVSUPSqmhGdOkPqBjK1qw'
        ];

        const host = process.env.NODE_ENV === 'production' ? '' : `https://${window.location.hostname}:5001`;
        this.connection = new signalR.HubConnectionBuilder()
          .withUrl(
            `${host}/hubs/meetingHub?access_token=${accessTokens[this.peerId]}`
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

        this.connection.on('Notify', async data => {
          await this.processNotification(data);
        });
        await this.connection.start();
        await this.start();
      } catch (e) {
        logger.debug(e.message);
      }
    },
    async start() {
      let result = await this.connection.invoke('GetRouterRtpCapabilities');
      if (result.code !== 200) {
        logger.error('processNotification() | GetRouterRtpCapabilities failure.');
        return;
      }

      const routerRtpCapabilities = result.data;
      this.mediasoupDevice = new mediasoupClient.Device();
      await this.mediasoupDevice.load({
        routerRtpCapabilities
      });

      // NOTE: Stuff to play remote audios due to browsers' new autoplay policy.
      //
      // Just get access to the mic and DO NOT close the mic track for a while.
      // Super hack!
      // {
      //   const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      //   const audioTrack = stream.getAudioTracks()[0];

      //   audioTrack.enabled = false;

      //   setTimeout(() => audioTrack.stop(), 120000);
      // }
      
      // GetRouterRtpCapabilities 成功, Join
      result = await this.connection.invoke('Join', {
        rtpCapabilities: this.mediasoupDevice.rtpCapabilities,
        sctpCapabilities: undefined, // 使用 DataChannel 并且会 consume 则取 this.mediasoupDevice.sctpCapabilities
        displayName: 'Guest',
        sources: ['mic', 'webcam'],
        appData: {}
      });
      if (result.code !== 200) {
        logger.error('processNotification() | Join failure.');
        return;
      }

      if(this.produce) {
        // Join成功，CreateWebRtcTransport(生产) 
        result = await this.connection.invoke('CreateWebRtcTransport', {
          forceTcp: false,
          producing: true,
          consuming: false,
          sctpCapabilities: undefined // 使用 DataChannel 并且会 consume 则取 this.mediasoupDevice.sctpCapabilities
        });
        if (result.code !== 200) {
          logger.error('processNotification() | CreateWebRtcTransport failure.');
          return;
        }

        // CreateWebRtcTransport(生产), createSendTransport
        this.sendTransport = this.mediasoupDevice.createSendTransport({
          id: result.data.transportId,
          iceParameters: result.data.iceParameters,
          iceCandidates: result.data.iceCandidates,
          dtlsParameters: result.data.dtlsParameters,
          sctpParameters: result.data.sctpParameters,
          iceServers: [],
          proprietaryConstraints: PC_PROPRIETARY_CONSTRAINTS
        });

        this.sendTransport.on(
          'connect',
          ({ dtlsParameters }, callback, errback) => {
            logger.debug('sendTransport.on() connect dtls: %o', dtlsParameters);
            this.connection
              .invoke('ConnectWebRtcTransport', {
                transportId: this.sendTransport.id,
                dtlsParameters
              })
              .then(callback)
              .catch(errback);
          }
        );

        this.sendTransport.on(
          'produce',
          // appData 需要包含 roomId 和 source
          // eslint-disable-next-line no-unused-vars
          async ({ kind, rtpParameters, appData }, callback, errback) => {
            logger.debug('sendTransport.on() produce, appData: %o', appData);
            try {
              const result = await this.connection.invoke('Produce', {
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

        this.sendTransport.on('connectionstatechange', state => {
          logger.debug(`sendTransport.on() connectionstatechange: ${state}`);
        });
      }
      // createSendTransport 成功, CreateWebRtcTransport(消费)
      result = await this.connection.invoke('CreateWebRtcTransport', {
        forceTcp: false,
        producing: false,
        consuming: true,
        sctpCapabilities: undefined // 使用 DataChannel 并且会 consume 则取 this.mediasoupDevice.sctpCapabilities
      });

      // CreateWebRtcTransport(消费)成功, createRecvTransport
      this.recvTransport = this.mediasoupDevice.createRecvTransport({
        id: result.data.transportId,
        iceParameters: result.data.iceParameters,
        iceCandidates: result.data.iceCandidates,
        dtlsParameters: result.data.dtlsParameters,
        sctpParameters: result.data.sctpParameters,
        iceServers: []
      });

      this.recvTransport.on(
        'connect',
        ({ dtlsParameters }, callback, errback) => {
          logger.debug('recvTransport.on() connect dtls: %o', dtlsParameters);
          this.connection
            .invoke('ConnectWebRtcTransport', {
              transportId: this.recvTransport.id,
              dtlsParameters
            })
            .then(callback)
            .catch(errback);
        }
      );

      this.recvTransport.on('connectionstatechange', state => {
        logger.debug(`recvTransport.on() connectionstatechange: ${state}`);
      });

      // createRecvTransport成功, JoinRoom
      result = await this.connection.invoke('JoinRoom', {
        roomId: '1'
      });
      if (result.code !== 200) {
        logger.error('processNotification() | JoinRoom failure.');
        return;
      }

      const joinRoomData = result.data;
      logger.debug('Peers:%o', joinRoomData.peers);

// 临时
if(this.peerId === '1000') {
      if(this.mediasoupDevice.canProduce('audio')) {
       await this.enableMic();
      }
      if(this.mediasoupDevice.canProduce('video')) {
       await this.enableWebcam();
      }
}
    },
    async processNewConsumer(data) {
      const {
        producerPeerId,
        producerId,
        consumerId,
        kind,
        rtpParameters,
        //type, // mediasoup-client 的 Transport.ts 不使用该参数
        producerAppData,
        //producerPaused // mediasoup-client 的 Transport.ts 不使用该参数
      } = data;

      const consumer = await this.recvTransport.consume({
        id: consumerId,
        producerId,
        kind,
        rtpParameters,
        appData: { ...producerAppData, producerPeerId } // Trick.
      });
      logger.debug('processNewConsumer() Consumer: %o', consumer);

      // Store in the map.
      this.consumers.set(consumer.id, consumer);

      consumer.on('transportclose', () => {
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
      if (kind === 'audio') {
        consumer.volume = 0;

        const stream = new MediaStream();

        stream.addTrack(consumer.track);

        if (!stream.getAudioTracks()[0]) {
          throw new Error(
            'request.newConsumer | given stream has no audio track'
          );
        }
      }
      */

      const stream = new MediaStream();
      stream.addTrack(consumer.track);

      this.$set(kind === 'video' ? this.remoteVideoStreams : this.remoteAudioStreams, consumerId, stream);

      // We are ready. Answer the request so the server will
      // resume this Consumer (which was paused for now).
      logger.debug('processNewConsumer() ResumeConsumer');
      const result = await this.connection.invoke('ResumeConsumer', consumerId);
      if (result.code !== 200) {
        logger.error('processNewConsumer() | ResumeConsumer failure.');
        return;
      }
    },
    async pull(roomId, producerPeerId, sources) {
      const result = await this.connection.invoke('Pull', {
        roomId,
        producerPeerId,
        sources
      });
      if (result.code !== 200) {
        logger.error('pull() | pull failure.');
        return;
      }
    },
    async processNotification(data) {
      logger.debug('processNotification() | %o', data);
      switch (data.type) {
        case 'newConsumer': {
            await this.processNewConsumer(data.data);
            
            break;
        }
        case 'producerScore': {
          // eslint-disable-next-line no-unused-vars
          const { producerId, score } = data.data;

          break;
        }

        case 'peerJoinRoom': {
          // eslint-disable-next-line no-unused-vars
          const peer = data.data;
          const {roomId, peerId, sources } = peer;
          
          await this.pull(roomId, peerId, sources);

          break;
        }

        case 'peerLeaveRoom':
        {
          // eslint-disable-next-line no-unused-vars
          const { peerId } = data.data;

          break;
        }
        
        case 'produceSources':
        {
          if(!this.produce) break;

          const { /*roomId, */produceSources } = data.data;
          for(let i =0; i < produceSources.length; i++){
            if(produceSources[i] === 'mic' && this.mediasoupDevice.canProduce('audio')) {
              await this.enableMic();
            } else if(produceSources[i] === 'webcam' && this.mediasoupDevice.canProduce('video')) {
              await this.enableWebcam();
            }
          }

          break;
        }

        case 'downlinkBwe':
        {
          logger.debug('\'downlinkBwe\' event:%o', data.data);

          break;
        }

        case 'consumerClosed': {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          this.$delete(consumer.kind === 'video' ? this.remoteVideoStreams : this.remoteAudioStreams, consumerId)
          consumer.close();
          this.consumers.delete(consumerId);

          break;
        }

        case 'consumerPaused': {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case 'consumerResumed': {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case 'consumerLayersChanged': {
          // eslint-disable-next-line no-unused-vars
          const { consumerId, spatialLayer, temporalLayer } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case 'consumerScore': {
          const { consumerId } = data.data;
          const consumer = this.consumers.get(consumerId);

          if (!consumer) break;

          break;
        }

        case 'peerLeave': {

          break;
        }

        default: {
          logger.error('unknown data.type, data:%o', data);
        }
      }
    },
    async enableMic() {
      logger.debug('enableMic()');

      if (this.micProducer) return;
      if (this.mediasoupDevice && !this.mediasoupDevice.canProduce('audio')) {
        logger.error('enableMic() | cannot produce audio');
        return;
      }

      let track;

      try {
        const deviceId = await this._getAudioDeviceId();

        const device = this.audioDevices[deviceId];

        if (!device) throw new Error('no audio devices');

        logger.debug(
          'enableMic() | new selected audio device [device:%o]',
          device
        );

        logger.debug('enableMic() | calling getUserMedia()');

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
          appData: { source: 'mic', roomId: '1' }
        });

        this.micProducer.on('transportclose', () => {
          this.micProducer = null;
        });

        this.micProducer.on('trackended', () => {
          this.disableMic().catch(() => {});
        });

        this.micProducer.volume = 0;
      } catch (error) {
        console.log('enableMic() failed:%o', error);
        logger.error('enableMic() failed:%o', error);
        if (track) track.stop();
      }
    },
    async disableMic() {
      logger.debug('disableMic()');
      if (!this.micProducer) return;

      this.micProducer.close();

      try {
        await this.connection.invoke('CloseProducer', {
          producerId: this.micProducer.id
        });
      } catch (error) {
        logger.error('disableMic() [error:"%o"]', error);
      }

      this.micProducer = null;
    },
    async enableWebcam() {
      logger.debug('enableWebcam()');

      if (this.webcamProducer) return;
      if (this.mediasoupDevice && !this.mediasoupDevice.canProduce('video')) {
        logger.error('enableWebcam() | cannot produce video');

        return;
      }

      let track;

      try {
        const deviceId = await this._getWebcamDeviceId();

        logger.debug(`_setWebcamProducer() | webcam: ${deviceId}`);

        const device = this.webcams.get(deviceId);

        if (!device) throw new Error(`no webcam devices: ${JSON.stringify(this.webcams)}`);

        logger.debug('_setWebcamProducer() | new selected webcam [device:%o]', device);

        logger.debug('_setWebcamProducer() | calling getUserMedia()');

        const stream = await navigator.mediaDevices.getUserMedia({ video: true })
        /*
        const stream = await navigator.mediaDevices.getUserMedia({
          video: {
            deviceId: { ideal: deviceId },
            ...VIDEO_CONSTRAINS.hd
          }
        });
        //*/
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
            c => c.kind === 'video'
          );

          if (firstVideoCodec.mimeType.toLowerCase() === 'video/vp9')
            encodings = WEBCAM_KSVC_ENCODINGS;
          else encodings = WEBCAM_SIMULCAST_ENCODINGS;
        }

        this.webcamProducer = await this.sendTransport.produce({
          track,
          encodings,
          codecOptions,
          codec,
          appData: { source: 'webcam', roomId: '1' }
        });

        this.webcamProducer.on('transportclose', () => {
          this.webcamProducer = null;
        });

        this.webcamProducer.on('trackended', () => {
          this.disableWebcam().catch(() => {});
        });
        logger.debug('_setWebcamProducer() succeeded');
      } catch (error) {
        logger.error('_setWebcamProducer() failed:%o', error);

        if (track) track.stop();
      }
    },
    async disableWebcam() {
      logger.debug('disableWebcam()');

      if (!this.webcamProducer) return;

      this.webcamProducer.close();

      try {
        await this.connection.invoke('CloseProducer', {
          producerId: this.webcamProducer.id
        });
      } catch (error) {
        logger.error('disableWebcam() [error:"%o"]', error);
      }

      this.webcamProducer = null;
    },
    async _updateAudioDevices() {
      logger.debug('_updateAudioDevices()');

      // Reset the list.
      this.audioDevices = {};

      try {
        logger.debug('_updateAudioDevices() | calling enumerateDevices()');

        const devices = await navigator.mediaDevices.enumerateDevices();

        for (const device of devices) {
          if (device.kind !== 'audioinput') continue;

          this.audioDevices[device.deviceId] = device;
        }
      } catch (error) {
        logger.error('_updateAudioDevices() failed:%o', error);
      }
    },
    async _updateWebcams() {
      logger.debug('_updateWebcams()');

      // Reset the list.
      this.webcams = new Map();

      try {
        logger.debug('_updateWebcams() | calling enumerateDevices()');

        const devices = await navigator.mediaDevices.enumerateDevices();

        logger.debug('_updateWebcams() | %o', devices);
        for (const device of devices) {
          if (device.kind !== 'videoinput') continue;
          logger.debug('_updateWebcams() | %o', device);
          this.webcams.set(device.deviceId, device);
        }
      } catch (error) {
        logger.error('_updateWebcams() failed:%o', error);
      }
    },
    async _getAudioDeviceId() {
      logger.debug('_getAudioDeviceId()');

      try {
        logger.debug('_getAudioDeviceId() | calling _updateAudioDeviceId()');

        await this._updateAudioDevices();

        const audioDevices = Object.values(this.audioDevices);
        return audioDevices[0] ? audioDevices[0].deviceId : null;
      } catch (error) {
        logger.error('_getAudioDeviceId() failed:%o', error);
      }
    },
    async _getWebcamDeviceId() {
      logger.debug('_getWebcamDeviceId()');

      try {
        logger.debug('_getWebcamDeviceId() | calling _updateWebcams()');

        await this._updateWebcams();

        const webcams = Array.from(this.webcams.values());
        return webcams[0] ? webcams[0].deviceId : null;
      } catch (error) {
        logger.error('_getWebcamDeviceId() failed:%o', error);
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
  font-family: 'Avenir', Helvetica, Arial, sans-serif;
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

video {
  width: 180px;
  background-color: #000;
}

 /* 水平镜像翻转 */
 /*
video#localVideo {
    transform: rotateY(180deg);  
}
*/
</style>
