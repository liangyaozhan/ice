// **********************************************************************
//
// Copyright (c) 2003
// ZeroC, Inc.
// Billerica, MA, USA
//
// All Rights Reserved.
//
// Ice is free software; you can redistribute it and/or modify it under
// the terms of the GNU General Public License version 2 as published by
// the Free Software Foundation.
//
// **********************************************************************

namespace IceInternal
{

    using System.Collections;
    using System.Diagnostics;

    public sealed class Connection : EventHandler
    {
	public void validate()
	{
	    lock(this)
	    {
	        Debug.Assert(_state == StateNotValidated);

		if(!endpoint().datagram()) // Datagram connections are always implicitly validated.
		{
		    try
		    {
			if(_adapter != null)
			{
			    lock(_sendMutex)
			    {
				//
				// Incoming connections play the active role with
				// respect to connection validation.
				//
				BasicStream os = new BasicStream(_instance);
				os.writeByte(Protocol.magic[0]);
				os.writeByte(Protocol.magic[1]);
				os.writeByte(Protocol.magic[2]);
				os.writeByte(Protocol.magic[3]);
				os.writeByte(Protocol.protocolMajor);
				os.writeByte(Protocol.protocolMinor);
				os.writeByte(Protocol.encodingMajor);
				os.writeByte(Protocol.encodingMinor);
				os.writeByte(Protocol.validateConnectionMsg);
				os.writeByte((byte)0); // Compression status.
				os.writeInt(Protocol.headerSize); // Message size.
				TraceUtil.traceHeader("sending validate connection", os, _logger, _traceLevels);
				_transceiver.write(os, _endpoint.timeout());
			    }
			}
			else
			{
			    //
			    // Outgoing connections play the passive role with
			    // respect to connection validation.
			    //
			    BasicStream ins = new BasicStream(_instance);
			    ins.resize(Protocol.headerSize, true);
			    ins.pos(0);
			    _transceiver.read(ins, _endpoint.timeout());
			    int pos = ins.pos();
			    Debug.Assert(pos >= Protocol.headerSize);
			    ins.pos(0);
			    byte[] m = new byte[4];
			    m[0] = ins.readByte();
			    m[1] = ins.readByte();
			    m[2] = ins.readByte();
			    m[3] = ins.readByte();
			    if(m[0] != Protocol.magic[0] || m[1] != Protocol.magic[1] ||
			       m[2] != Protocol.magic[2] || m[3] != Protocol.magic[3])
			    {
				Ice.BadMagicException ex = new Ice.BadMagicException();
				ex.badMagic = m;
				throw ex;
			    }
			    byte pMajor = ins.readByte();
			    byte pMinor = ins.readByte();
			    
			    //
			    // We only check the major version number
			    // here. The minor version number is irrelevant --
			    // no matter what minor version number is offered
			    // by the server, we can be certain that the
			    // server supports at least minor version 0.  As
			    // the client, we are obliged to never produce a
			    // message with a minor version number that is
			    // larger than what the server can understand, but
			    // we don't care if the server understands more
			    // than we do.
			    //
			    // Note: Once we add minor versions, we need to
			    // modify the client side to never produce a
			    // message with a minor number that is greater
			    // than what the server can handle. Similarly, the
			    // server side will have to be modified so it
			    // never replies with a minor version that is
			    // greater than what the client can handle.
			    //
			    if(pMajor != Protocol.protocolMajor)
			    {
				Ice.UnsupportedProtocolException e = new Ice.UnsupportedProtocolException();
				e.badMajor = pMajor < 0 ? pMajor + 255 : pMajor;
				e.badMinor = pMinor < 0 ? pMinor + 255 : pMinor;
				e.major = Protocol.protocolMajor;
				e.minor = Protocol.protocolMinor;
				throw e;
			    }
			    
			    byte eMajor = ins.readByte();
			    byte eMinor = ins.readByte();
			    
			    //
			    // The same applies here as above -- only the
			    // major version number of the encoding is
			    // relevant.
			    //
			    if(eMajor != Protocol.encodingMajor)
			    {
				Ice.UnsupportedEncodingException e = new Ice.UnsupportedEncodingException();
				e.badMajor = eMajor < 0 ? eMajor + 255 : eMajor;
				e.badMinor = eMinor < 0 ? eMinor + 255 : eMinor;
				e.major = Protocol.encodingMajor;
				e.minor = Protocol.encodingMinor;
				throw e;
			    }
			    
			    byte messageType = ins.readByte();
			    if(messageType != Protocol.validateConnectionMsg)
			    {
				throw new Ice.ConnectionNotValidatedException();
			    }
			    
			    byte compress = ins.readByte();
			    if(compress == (byte)2)
			    {
				throw new Ice.CompressionNotSupportedException();
			    }
			    
			    int size = ins.readInt();
			    if(size != Protocol.headerSize)
			    {
				throw new Ice.IllegalMessageSizeException();
			    }
			    TraceUtil.traceHeader("received validate connection", ins, _logger, _traceLevels);
			}
		    }
		    catch(Ice.LocalException ex)
		    {
			setState(StateClosed, ex);
			Debug.Assert(_exception != null);
			throw _exception;
		    }
		}
		
		if(_acmTimeout > 0)
		{
		    long _acmAbsolutetimoutMillis = System.DateTime.Now.Ticks / 10 + _acmTimeout * 1000;
		}

		//
		// We start out in holding state.
		//
		setState(StateHolding);
	    }
	}
	
	public void activate()
	{
	    lock(this)
	    {
		setState(StateActive);
	    }
	}
	
	public void hold()
	{
	    lock(this)
	    {
		setState(StateHolding);
	    }
	}
	
	// DestructionReason.
	public const int ObjectAdapterDeactivated = 0;
	public const int CommunicatorDestroyed = 1;
	
	public void destroy(int reason)
	{
	    lock(this)
	    {
		switch(reason)
		{
		    case ObjectAdapterDeactivated: 
		    {
			setState(StateClosing, new Ice.ObjectAdapterDeactivatedException());
			break;
		    }
		    
		    case CommunicatorDestroyed: 
		    {
			setState(StateClosing, new Ice.CommunicatorDestroyedException());
			break;
		    }
		}
	    }
	}
	
	public bool isValidated()
	{
	    lock(this)
	    {
		return _state > StateNotValidated;
	    }
	}

	public bool isDestroyed()
	{
	    lock(this)
	    {
		return _state >= StateClosing;
	    }
	}

	public bool isFinished()
	{
	    lock(this)
	    {
		return _transceiver == null && _dispatchCount == 0;
	    }
	}

	public void waitUntilHolding()
	{
	    lock(this)
	    {
		while(_state < StateHolding || _dispatchCount > 0)
		{
		    try
		    {
			System.Threading.Monitor.Wait(this);
		    }
		    catch(System.Threading.ThreadInterruptedException)
		    {
		    }
		}
	    }
	}
	
	public void waitUntilFinished()
	{
	    lock(this)
	    {
		//
		// We wait indefinitely until connection closing has been
		// initiated. We also wait indefinitely until all outstanding
		// requests are completed. Otherwise we couldn't guarantee
		// that there are no outstanding calls when deactivate() is
		// called on the servant locators.
		//
		while(_state < StateClosing || _dispatchCount > 0)
		{
		    try
		    {
			System.Threading.Monitor.Wait(this);
		    }
		    catch(System.Threading.ThreadInterruptedException)
		    {
		    }
		}
		
		//
		// Now we must wait until close() has been called on the
		// transceiver.
		//
		while(_transceiver != null)
		{
		    try
		    {
			if(_state != StateClosed && _endpoint.timeout() >= 0)
			{
			    long absoluteWaitTime = _stateTime + _endpoint.timeout();
			    int waitTime = (int)(absoluteWaitTime - System.DateTime.Now.Ticks / 10);
			    
			    if(waitTime > 0)
			    {
			        //
				// We must wait a bit longer until we close
				// this connection.
				//
				System.Threading.Monitor.Wait(this, waitTime);
				if(System.DateTime.Now.Ticks / 10 >= absoluteWaitTime)
				{
				    setState(StateClosed, new Ice.CloseTimeoutException());
				}
			    }
			    else
			    {
				//
			        // We already waited long enough, so let's
				// close this connection!
				//
				setState(StateClosed, new Ice.CloseTimeoutException());
			    }

			    //
			    // No return here, we must still wait until
			    // close() is called on the _transceiver.
			    //
			}
			else
			{
			    System.Threading.Monitor.Wait(this);
			}
		    }
		    catch(System.Threading.ThreadInterruptedException)
		    {
		    }
		}
	    }

	    Debug.Assert(_state == StateClosed);
	}
	
	public void monitor()
	{
	    lock(this)
	    {
		if(_state != StateActive)
		{
		    return;
		}
		
		//
		// Check for timed out async requests.
		//
		foreach(OutgoingAsync og in _asyncRequests.Values)
		{
		    if(og.__timedOut())
		    {
			setState(StateClosed, new Ice.TimeoutException());
			return;
		    }
		}
		
		//
		// Active connection management for idle connections.
		//
		//
		if(_acmTimeout > 0 &&
		   _requests.Count == 0 && _asyncRequests.Count == 0 &&
		   !_batchStreamInUse && _batchStream.isEmpty() &&
		   _dispatchCount == 0)
		{
		    if(System.DateTime.Now.Ticks / 10 >= _acmAbsoluteTimeoutMillis)
		    {
			setState(StateClosing, new Ice.ConnectionTimeoutException());
			return;
		    }
		}
	    }
	}
	
	private static readonly byte[] _requestHdr = new byte[]
	{
	    Protocol.magic[0], Protocol.magic[1], Protocol.magic[2], Protocol.magic[3],
	    Protocol.protocolMajor, Protocol.protocolMinor,
	    Protocol.encodingMajor, Protocol.encodingMinor,
	    Protocol.requestMsg,
	    (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0
	};
	
	//
	// TODO:  Should not be a member function of Connection.
	//
	public void prepareRequest(BasicStream os)
	{
	    os.writeBlob(_requestHdr);
	}
	
	public void sendRequest(BasicStream os, Outgoing og)
	{
	    int requestId = 0;

	    lock(this)
	    {
		Debug.Assert(!(og != null && _endpoint.datagram())); // Twoway requests cannot be datagrams.

		if(_exception != null)
		{
		    throw _exception;
		}

		Debug.Assert(_state > StateNotValidated);
		Debug.Assert(_state < StateClosing);
		
		//
		// Fill in the message size.
		//
		os.pos(10);
		os.writeInt(os.size());

		//
		// Only add to the request map if this is a twoway call.
		//
		if(og != null)
		{
		    //
		    // Create a new unique request ID.
		    //
		    if(requestId <= 0)
		    {
			_nextRequestId = 1;
			requestId = _nextRequestId++;
		    }
		    
		    //
		    // Fill in the request ID.
		    //
		    os.pos(Protocol.headerSize);
		    os.writeInt(requestId);

		    //
		    // Add ot the requests map.
		    //
		    _requests[requestId] = og;
		}
		
		if(_acmTimeout > 0)
		{
		    _acmAbsoluteTimeoutMillis = System.DateTime.Now.Ticks / 10 + _acmTimeout * 1000;
		}
	    }
	    
	    try
	    {
	        lock(_sendMutex)
		{
		    if(_transceiver == null) // Has the transceiver already been closed?
		    {
		        Debug.Assert(_exception != null);
			throw _exception; // The exception is immutable at this point.
		    }

		    //
		    // Send the request.
		    //
		    TraceUtil.traceRequest("sending request", os, _logger, _traceLevels);
		    _transceiver.write(os, _endpoint.timeout());
		}
	    }
	    catch(Ice.LocalException ex)
	    {
	        lock(this)
		{
		    setState(StateClosed, ex);
		    Debug.Assert(_exception != null);

		    if(og != null)
		    {
			//
			// If the request has already been removed from
			// the request map, we are out of luck. It would
			// mean that finished() has been called already,
			// and therefore the exception has been set using
			// the Outgoing::finished() callback. In this
			// case, we cannot throw the exception here,
			// because we must not both raise an exception and
			// have Outgoing::finished() called with an
			// exception. This means that in some rare cases,
			// a request will not be retried even though it
			// could. But I honestly don't know how I could
			// avoid this, without a very elaborate and
			// complex design, which would be bad for
			// performance.
			//
			Outgoing o = (Outgoing)_requests[requestId];
			_requests.Remove(requestId);
			if(o != null)
			{
			    Debug.Assert(o == og);
			    throw _exception;
			}
		    }
		    else
		    {
		        throw _exception;
		    }
		}
	    }
	}
	
	public void sendAsyncRequest(BasicStream os, OutgoingAsync og)
	{
	    int requestId = 0;

	    lock(this)
	    {
	        Debug.Assert(!_endpoint.datagram()); // Twoway requests cannot be datagrams, and async implies twoway.

		if(_exception != null)
		{
		    throw _exception;
		}

		Debug.Assert(_state > StateNotValidated);
		Debug.Assert(_state < StateClosing);
		
		//
		// Fill in the message size.
		//
		os.pos(10);
		os.writeInt(os.size());
		    
		//
		// Create a new unique request ID.
		//
		requestId = _nextRequestId++;
		if(requestId <= 0)
		{
		    _nextRequestId = 1;
		    requestId = _nextRequestId++;
		}

		//
		// Fill in the request ID.
		//
		os.pos(Protocol.headerSize);
		os.writeInt(requestId);

		//
		// Add to the requests map.
		//
		_asyncRequests[requestId] = og;
		    
		if(_acmTimeout > 0)
		{
		    _acmAbsoluteTimeoutMillis = System.DateTime.Now.Ticks / 10  + _acmTimeout * 1000;
		}
	    }

	    try
	    {
	        lock(_sendMutex)
		{
		    if(_transceiver == null) // Has the transceiver already been closed?
		    {
		        Debug.Assert(_exception != null);
			throw _exception; // The exception is imuutable at this point.
		    }

		    //
		    // Send the request.
		    //
		    TraceUtil.traceRequest("sending asynchronous request", os, _logger, _traceLevels);
		    _transceiver.write(os, _endpoint.timeout());
		}
	    }
	    catch(Ice.LocalException ex)
	    {
	        lock(this)
		{
		    setState(StateClosed, ex);
		    Debug.Assert(_exception != null);
		    
		    //
		    // If the request has already been removed from the
		    // async request map, we are out of luck. It would
		    // mean that finished() has been called already, and
		    // therefore the exception has been set using the
		    // OutgoingAsync::__finished() callback. In this case,
		    // we cannot throw the exception here, because we must
		    // not both raise an exception and have
		    // OutgoingAsync::__finished() called with an
		    // exception. This means that in some rare cases, a
		    // request will not be retried even though it
		    // could. But I honestly don't know how I could avoid
		    // this, without a very elaborate and complex design,
		    // which would be bad for performance.
		    //
		    OutgoingAsync o = (OutgoingAsync)_asyncRequests[requestId];
		    _asyncRequests.Remove(requestId);
		    if(o != null)
		    {
			Debug.Assert(o == og);
			throw _exception;
		    }
	    	}
	    }
	}
	
	private static readonly byte[] _requestBatchHdr = new byte[]
	{
	    Protocol.magic[0], Protocol.magic[1], Protocol.magic[2], Protocol.magic[3],
	    Protocol.protocolMajor, Protocol.protocolMinor,
	    Protocol.encodingMajor, Protocol.encodingMinor,
	    Protocol.requestBatchMsg,
	    0,
	    (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0
	};
	
	public void prepareBatchRequest(BasicStream os)
	{
	    lock(this)
	    {
		while(_batchStreamInUse && _exception == null)
		{
		    try
		    {
			System.Threading.Monitor.Wait(this);
		    }
		    catch(System.Threading.ThreadInterruptedException)
		    {
		    }
		}
		
		if(_exception != null)
		{
		    throw _exception;
		}

		Debug.Assert(_state > StateNotValidated);
		Debug.Assert(_state < StateClosing);
		
		if(_batchStream.isEmpty())
		{
		    try
		    {
			_batchStream.writeBlob(_requestBatchHdr);
		    }
		    catch(Ice.LocalException ex)
		    {
			setState(StateClosed, ex);
			throw ex;
		    }
		}
		
		_batchStreamInUse = true;
		_batchStream.swap(os);
		
		//
		// _batchStream now belongs to the caller, until
		// finishBatchRequest() is called.
		//
	    }
	}
	
	public void finishBatchRequest(BasicStream os)
	{
	    lock(this)
	    {
		if(_exception != null)
		{
		    throw _exception;
		}

		Debug.Assert(_state > StateNotValidated);
		Debug.Assert(_state < StateClosing);
		
		_batchStream.swap(os); // Get the batch stream back.
		++_batchRequestNum; // Increment the number of requests in the batch.
		
		//
		// Give the Connection back.
		//
		Debug.Assert(_batchStreamInUse);
		_batchStreamInUse = false;
		System.Threading.Monitor.PulseAll(this);
	    }
	}
	
	public void flushBatchRequest()
	{
	    lock(this)
	    {
		while(_batchStreamInUse && _exception == null)
		{
		    try
		    {
			System.Threading.Monitor.Wait(this);
		    }
		    catch(System.Threading.ThreadInterruptedException)
		    {
		    }
		}
		
		if(_exception != null)
		{
		    throw _exception;
		}

		Debug.Assert(_state > StateNotValidated);
		Debug.Assert(_state < StateClosing);
		
		if(_batchStream.isEmpty())
		{
		    return; // Nothing to do.
		}
			
		//
		// Fill in the message size.
		//
		_batchStream.pos(10);
		_batchStream.writeInt(_batchStream.size());
			
		//
		// Fill in the number of requests in the batch.
		//
		_batchStream.writeInt(_batchRequestNum);
			
		if(_acmTimeout > 0)
		{
		    _acmAbsoluteTimeoutMillis = System.DateTime.Now.Ticks / 10 + _acmTimeout * 1000;
		}

		//
		// Prevent that new batch requests are added while we are
		// flushing.
		//
		_batchStreamInUse = true;
	    }

	    try
	    {
	        lock(_sendMutex)
		{
		    if(_transceiver == null) // Has the transceiver already been closed?
		    {
		        Debug.Assert(_exception != null);
			throw _exception; // The exception is immutable at this point.
		    }

		    //
		    // Send the batch request.
		    //
		    TraceUtil.traceBatchRequest("sending batch request", _batchStream, _logger, _traceLevels);
		    _transceiver.write(_batchStream, _endpoint.timeout());
		}
	    }
	    catch(Ice.LocalException ex)
	    {
	        lock(this)
		{
		    setState(StateClosed, ex);
		    Debug.Assert(_exception != null);

		    //
		    // Since batch requests area all oneways (or datarams), we
		    // must report the exception to the caller.
		    //
		    throw _exception;
		}
	    }

	    lock(this)
	    {
	        //
		// Reset the batch stream, and notify that flushing is over.
		//
		_batchStream.destroy();
		_batchStream = new BasicStream(_instance);
		_batchRequestNum = 0;
		_batchStreamInUse = false;
		System.Threading.Monitor.PulseAll(this);
	    }
	}
	
	public void sendResponse(BasicStream os, byte compress)
	{
	    try
	    {
		lock(_sendMutex)
		{
		    if(_transceiver == null) // Ha sthe transceiver already been closed?
		    {
		        Debug.Assert(_exception != null);
			throw _exception; // The exception is immutable at this point.
		    }

		    //
		    // Fill in the message size.
		    //
		    os.pos(10);
		    os.writeInt(os.size());
		    
		    //
		    // Send the reply.
		    //
		    TraceUtil.traceReply("sending reply", os, _logger, _traceLevels);
		    _transceiver.write(os, _endpoint.timeout());
		}
	    }
	    catch(Ice.LocalException ex)
	    {
	        lock(this)
		{
		    setState(StateClosed, ex);
		}
	    }

	    lock(this)
	    {
	        Debug.Assert(_state > StateNotValidated);

		try
		{
		    if(--_dispatchCount == 0)
		    {
		        System.Threading.Monitor.PulseAll(this);
		    }

		    if(_state == StateClosing && _dispatchCount == 0)
		    {
		        initiateShutdown();
		    }
		
		    if(_acmTimeout > 0)
		    {
			_acmAbsoluteTimeoutMillis = System.DateTime.Now.Ticks / 10 + _acmTimeout * 1000;
		    }
		}
		catch(Ice.LocalException ex)
		{
		    setState(StateClosed, ex);
		}
	    }
	}
	
	public void sendNoResponse()
	{
	    Debug.Assert(_state > StateNotValidated);

	    lock(this)
	    {
		try
		{
		    if(--_dispatchCount == 0)
		    {
			System.Threading.Monitor.PulseAll(this);
		    }
		    
		    if(_state == StateClosing && _dispatchCount == 0)
		    {
			initiateShutdown();
		    }
		}
		catch(Ice.LocalException ex)
		{
		    setState(StateClosed, ex);
		}
	    }
	}
	
	public int timeout()
	{
	    // No mutex protection necessary, _endpoint is immutable.
	    return _endpoint.timeout();
	}
	
	public Endpoint endpoint()
	{
	    // No mutex protection necessary, _endpoint is immutable.
	    return _endpoint;
	}

	public void setAdapter(Ice.ObjectAdapter adapter)
	{
	    lock(this)
	    {
		//
		// We never change the thread pool with which we were
		// initially registered, even if we add or remove an object
		// adapter.
		//
		
		_adapter = adapter;
		if(_adapter != null)
		{
		    _servantManager = ((Ice.ObjectAdapterI) _adapter).getServantManager();
		}
		else
		{
		    _servantManager = null;
		}
	    }
	}

	public Ice.ObjectAdapter getAdapter()
	{
	    lock(this)
	    {
		return _adapter;
	    }
	}
	
	//
	// Operations from EventHandler
	//
	
	public override bool datagram()
	{
	    return _endpoint.datagram();
	}
	
	public override bool readable()
	{
	    return true;
	}
	
	public override void read(BasicStream stream)
	{
	    if(_transceiver != null)
	    {
		_transceiver.read(stream, 0);
	    }
	    
	    //
	    // Updating _acmAbsoluteTimeoutMillis is to expensive here,
	    // because we would have to acquire a lock just for this
	    // purpose. Instead, we update _acmAbsoluteTimeoutMillis in
	    // message().
	    //
	}
	
	private static readonly byte[] _replyHdr = new byte[]
	{
	    Protocol.magic[0], Protocol.magic[1], Protocol.magic[2], Protocol.magic[3],
	    Protocol.protocolMajor, Protocol.protocolMinor,
	    Protocol.encodingMajor, Protocol.encodingMinor,
	    Protocol.replyMsg,
	    (byte)0, (byte)0, (byte)0, (byte)0, (byte)0
	};
	
	public override void message(BasicStream stream, ThreadPool threadPool)
	{
	    OutgoingAsync outAsync = null;
	    int invoke = 0;
	    int requestId = 0;
	    byte compress = 0;
	    
	    lock(this)
	    {
	        //
		// We must promote with the synchronization, otherwise
		// there could be various race conditions with close
		// connection messages and other messages.
		//
		threadPool.promoteFollower();
		
		Debug.Assert(_state > StateNotValidated);

		if(_state == StateClosed)
		{
		    return;
		}
		
		if(_acmTimeout > 0)
		{
		    _acmAbsoluteTimeoutMillis = System.DateTime.Now.Ticks / 10 + _acmTimeout * 1000;
		}
		
		try
		{
		    //
		    // We don't need to check magic and version here. This
		    // has already been done by the ThreadPool, which
		    // provides us the stream.
		    //
		    Debug.Assert(stream.pos() == stream.size());
		    stream.pos(8);
		    byte messageType = stream.readByte();
		    compress = stream.readByte();
		    if(compress == (byte)2)
		    {
			throw new Ice.CompressionNotSupportedException();
		    }
		    stream.pos(Protocol.headerSize);
		    
		    switch(messageType)
		    {
		        case Protocol.closeConnectionMsg:
			{
			    TraceUtil.traceHeader("received close connection", stream, _logger, _traceLevels);
			    if(_endpoint.datagram() && _warn)
			    {
			        _logger.warning("ignoring close connection message for datagram connection:\n" + _desc);
			    }
			    else
			    {
			        setState(StateClosed, new Ice.CloseConnectionException());
			    }
			    break;
			}
			case Protocol.requestMsg: 
			{
			    if(_state == StateClosing)
			    {
				TraceUtil.traceRequest("received request during closing\n"
						       + "(ignored by server, client will retry)",
						       stream, _logger, _traceLevels);
			    }
			    else
			    {
				TraceUtil.traceRequest("received request", stream, _logger, _traceLevels);
				requestId = stream.readInt();
				invoke = 1;
				++_dispatchCount;
			    }
			    break;
			}
			
			case Protocol.requestBatchMsg: 
			{
			    if(_state == StateClosing)
			    {
				TraceUtil.traceBatchRequest("received batch request during closing\n"
							    + "(ignored by server, client will retry)",
							    stream, _logger, _traceLevels);
			    }
			    else
			    {
				TraceUtil.traceBatchRequest("received batch request", stream, _logger, _traceLevels);
				invoke = stream.readInt();
				if(invoke < 0)
				{
				    throw new Ice.NegativeSizeException();
				}
				_dispatchCount += invoke;
			    }
			    break;
			}
			
			case Protocol.replyMsg: 
			{
			    TraceUtil.traceReply("received reply", stream, _logger, _traceLevels);
			    requestId = stream.readInt();
			    Outgoing og = (Outgoing)_requests[requestId];
			    _requests.Remove(requestId);
			    if(og != null)
			    {
				og.finished(stream);
			    }
			    else
			    {
				outAsync = (OutgoingAsync)_asyncRequests[requestId];
				_asyncRequests.Remove(requestId);
				if(outAsync == null)
				{
				    throw new Ice.UnknownRequestIdException();
				}
			    }
			    break;
			}
			
			case Protocol.validateConnectionMsg: 
			{
			    TraceUtil.traceHeader("received validate connection", stream, _logger, _traceLevels);
			    if(_warn)
			    {
				_logger.warning("ignoring unexpected validate connection message:\n" + _desc);
			    }
			    break;
			}
			
			default: 
			{
			    TraceUtil.traceHeader("received unknown message\n" +
			                          "(invalid, closing connection)",
						  stream, _logger, _traceLevels);
			    throw new Ice.UnknownMessageException();
			}
		    }
		}
		catch(Ice.LocalException ex)
		{
		    setState(StateClosed, ex);
		    return;
		}
	    }
	    
	    //
	    // Asynchronous replies must be handled outside the thread
	    // synchronization, so that nested calls are possible.
	    //
	    if(outAsync != null)
	    {
		outAsync.__finished(stream);
	    }
	    
	    //
	    // Method invocation (or multiple invocations for batch messages)
	    // must be done outside the thread synchronization, so that nested
	    // calls are possible.
	    //
	    Incoming inc = null;
	    try
	    {
		while(invoke-- > 0)
		{
		    //
		    // Prepare the invocation.
		    //
		    bool response = !_endpoint.datagram() && requestId != 0;
		    inc = getIncoming(response, compress);
		    BasicStream ins = inc.istr();
		    stream.swap(ins);
		    BasicStream os = inc.ostr();
		    
		    //
		    // Prepare the response if necessary.
		    //
		    if(response)
		    {
			Debug.Assert(invoke == 0); // No further invocations if a response is expected.
			os.writeBlob(_replyHdr);
			
			//
			// Fill in the request ID.
			//
			os.writeInt(requestId);
		    }
		    
		    inc.invoke(_servantManager);
		    
		    //
		    // If there are more invocations, we need the stream back.
		    //
		    if(invoke > 0)
		    {
			stream.swap(ins);
		    }
		    
		    reclaimIncoming(inc);
		    inc = null;
		}
	    }
	    catch(Ice.LocalException ex)
	    {
		lock(this)
		{
		    setState(StateClosed, ex);
		}
	    }
	    catch(System.Exception ex)
	    {
		//
		// For other errors, we don't kill the whole
		// process, but just print the stack trace and close the
		// connection.
		//
		warning("closing connection", ex);
		lock(this)
		{
		    setState(StateClosed, new Ice.UnknownException());
		}
	    }
	    finally
	    {
		if(inc != null)
		{
		    reclaimIncoming(inc);
		}
	    }
	}
	
	public override void finished(ThreadPool threadPool)
	{
	    threadPool.promoteFollower();
	    
	    Ice.LocalException exception = null;

	    Hashtable requests = null;
	    Hashtable asyncRequests = null;
	    Incoming inc = null;

	    lock(this)
	    {
		if(_state == StateActive || _state == StateClosing)
		{
		    registerWithPool();
		}
		else if(_state == StateClosed)
		{
		    //
		    // We must make sure that nobody is sending when we
		    // close the transeiver.
		    //
		    lock(_sendMutex)
		    {
		        try
			{
			    _transceiver.close();
			}
			catch(Ice.LocalException ex)
			{
			    exception = ex;
			}

			_transceiver = null;
			System.Threading.Monitor.PulseAll(this);
		    }

		    //
		    // We must destroy the incoming cache. It is now not
		    // needed anymore.
		    //
		    lock(_incomingCacheMutex)
		    {
			Debug.Assert(_dispatchCount == 0);
			inc = _incomingCache;
			_incomingCache = null;
		    }
		}

		if(_state == StateClosed || _state == StateClosing)
		{
		    requests = _requests;
		    _requests = new Hashtable();

		    asyncRequests = _asyncRequests;
		    _asyncRequests = new Hashtable();
		}
	    }
	    while(inc != null)
	    {
		inc.__destroy();
		inc = inc.next;
	    }

	    if(requests != null)
	    {
	        foreach(Outgoing og in requests.Values)
		{
		    og.finished(_exception); // The exception is immutable at this point.
		}
	    }

	    if(asyncRequests != null)
	    {
	        foreach(OutgoingAsync og in asyncRequests.Values)
		{
		    og.__finished(_exception); // The exception is immutable at this point.
		}
	    }

	    if(exception != null)
	    {
	        throw exception;
	    }
	}
	
	public override void exception(Ice.LocalException ex)
	{
	    lock(this)
	    {
		setState(StateClosed, ex);
	    }
	}
	
	public override string ToString()
	{
	    return _desc; // No mutex lock, _desc is immutable.
	}
	
	internal Connection(Instance instance, Transceiver transceiver,
			    Endpoint endpoint, Ice.ObjectAdapter adapter)
	    : base(instance)
	{
	    _transceiver = transceiver;
	    _desc = transceiver.ToString();
	    _endpoint = endpoint;
	    _adapter = adapter;
	    _logger = instance.logger(); // Cached for better performance.
	    _traceLevels = instance.traceLevels(); // Cached for better performance.
	    _registeredWithPool = false;
	    _warn = _instance.properties().getPropertyAsInt("Ice.Warn.Connections") > 0;
	    _acmTimeout = _endpoint.datagram() ? 0 : _instance.connectionIdleTime();
	    _acmAbsoluteTimeoutMillis = 0;
	    _nextRequestId = 1;
	    _batchStream = new BasicStream(instance);
	    _batchStreamInUse = false;
	    _batchRequestNum = 0;
	    _dispatchCount = 0;
	    _state = StateNotValidated;
	    _stateTime = System.DateTime.Now.Ticks / 10;
	    
	    if(_adapter != null)
	    {
		_threadPool = ((Ice.ObjectAdapterI) _adapter).getThreadPool();
		_servantManager = ((Ice.ObjectAdapterI) _adapter).getServantManager();
	    }
	    else
	    {
		_threadPool = _instance.clientThreadPool();
		_servantManager = null;
	    }
	}
	
	~Connection()
	{
	    Debug.Assert(_state == StateClosed);
	    Debug.Assert(_transceiver == null);
	    Debug.Assert(_dispatchCount == 0);
	    Debug.Assert(_incomingCache == null);

	    _batchStream.destroy();
	}
	
	private const int StateNotValidated = 0;
	private const int StateActive = 1;
	private const int StateHolding = 2;
	private const int StateClosing = 3;
	private const int StateClosed = 4;
	
	private void setState(int state, Ice.LocalException ex)
	{
	    //
	    // If setState() is called with an exception, then only closed
	    // and closing states are permissible.
	    //
	    Debug.Assert(state == StateClosing || state == StateClosed);

	    if(_state == state) // Don't switch twice.
	    {
		return;
	    }
	    
	    if(_exception == null)
	    {
		_exception = ex;
		
		if(_warn)
		{
		    //
		    // We don't warn if we are not validated.
		    //
		    if(_state > StateNotValidated)
		    {
		        //
			// Don't warn about certain expected exceptions.
			//
			if(!(_exception is Ice.CloseConnectionException ||
			      _exception is Ice.ConnectionTimeoutException ||
			      _exception is Ice.CommunicatorDestroyedException ||
			      _exception is Ice.ObjectAdapterDeactivatedException ||
			      (_exception is Ice.ConnectionLostException && _state == StateClosing)))
			{
			    warning("connection exception", _exception);
			}
		    }
		}
	    }
	    
	    //
	    // We must set the new state before we notify requests of any
	    // exceptions. Otherwise new requests may retry on a
	    // connection that is not yet marked as closed or closing.
	    //
	    setState(state);
	}

	private void setState(int state)
	{
	    //
	    // We don't want to send close connection messages if the endpoint
	    // only supports oneway transmission from client to server.
	    //
	    if(_endpoint.datagram() && state == StateClosing)
	    {
		state = StateClosed;
	    }
	    
	    if(_state == state) // Don't switch twice.
	    {
		return;
	    }
	    
	    switch(state)
	    {
		case StateNotValidated: 
		{
		    Debug.Assert(false);
		    break;
		}
		
		case StateActive: 
		{
		    //
		    // Can only switch from holding or not validated to
		    // active.
		    //
		    if(_state != StateHolding && _state != StateNotValidated)
		    {
			return;
		    }
		    registerWithPool();
		    break;
		}
		
		case StateHolding: 
		{
		    //
		    // Can only switch from active or not validated to
		    // holding.
		    //
		    if(_state != StateActive && _state != StateNotValidated)
		    {
			return;
		    }
		    unregisterWithPool();
		    break;
		}
		
		case StateClosing: 
		{
		    //
		    // Can't change back from closed.
		    //
		    if(_state == StateClosed)
		    {
			    return;
		    }
		    registerWithPool(); // We need to continue to read in closing state.
		    break;
		}
		
		case StateClosed: 
		{
		    //
		    // If we change from not validated, we can close right
		    // away. Otherwise we first must make sure that we are
		    // registered, then we unregister, and let finished()
		    // do the close.
		    //
		    if(_state == StateNotValidated)
		    {
			Debug.Assert(!_registeredWithPool);

			//
			// We must make sure that nobidy is sending when
			// we close the transceiver.
			//
			lock(_sendMutex)
			{
			    try
			    {
				_transceiver.close();
			    }
			    catch(Ice.LocalException)
			    {
			        // Here we ignore any exceptions in close().
			    }

			    _transceiver = null;
			    //System.Threading.Monitor.PulseAll(); // We notify already below.
			}
		    }
		    else
		    {
			registerWithPool();
			unregisterWithPool();
		    }
		    break;
		}
	    }
	    
	    _state = state;
	    _stateTime = System.DateTime.Now.Ticks / 10;
	    System.Threading.Monitor.PulseAll(this);
	    
	    if(_state == StateClosing && _dispatchCount == 0)
	    {
		try
		{
		    initiateShutdown();
		}
		catch(Ice.LocalException ex)
		{
		    setState(StateClosed, ex);
		}
	    }
	}
	
	private void initiateShutdown()
	{
	    Debug.Assert(_state == StateClosing);
	    Debug.Assert(_dispatchCount == 0);
	    
	    if(!_endpoint.datagram())
	    {
	        lock(_sendMutex)
		{
		    //
		    // Before we shut down, we send a close connection
		    // message.
		    //
		    BasicStream os = new BasicStream(_instance);
		    os.writeByte(Protocol.magic[0]);
		    os.writeByte(Protocol.magic[1]);
		    os.writeByte(Protocol.magic[2]);
		    os.writeByte(Protocol.magic[3]);
		    os.writeByte(Protocol.protocolMajor);
		    os.writeByte(Protocol.protocolMinor);
		    os.writeByte(Protocol.encodingMajor);
		    os.writeByte(Protocol.encodingMinor);
		    os.writeByte(Protocol.closeConnectionMsg);
		    os.writeByte((byte)0); // Compression status.
		    os.writeInt(Protocol.headerSize); // Message size.

		    //
		    // Send the message.
		    //
		    TraceUtil.traceHeader("sending close connection", os, _logger, _traceLevels);
		    _transceiver.write(os, _endpoint.timeout());
		    _transceiver.shutdown();
		}
	    }
	}
	
	private void registerWithPool()
	{
	    if(!_registeredWithPool)
	    {
		_threadPool.register(_transceiver.fd(), this);
		_registeredWithPool = true;
		
		ConnectionMonitor connectionMonitor = _instance.connectionMonitor();
		if(connectionMonitor != null)
		{
		    connectionMonitor.add(this);
		}
	    }
	}
	
	private void unregisterWithPool()
	{
	    if(_registeredWithPool)
	    {
		_threadPool.unregister(_transceiver.fd());
		_registeredWithPool = false;
		
		ConnectionMonitor connectionMonitor = _instance.connectionMonitor();
		if(connectionMonitor != null)
		{
		    connectionMonitor.remove(this);
		}
	    }
	}
	
	private void warning(string msg, System.Exception ex)
	{
	    _logger.warning(msg + ":\n" + ex + "\n" + _transceiver.ToString());
	}
	
	private Incoming getIncoming(bool response, byte compress)
	{
	    Incoming inc = null;
	    
	    _incomingCacheMutex.WaitOne();
	    try
	    {
		if(_incomingCache == null)
		{
		    inc = new Incoming(_instance, this, _adapter, response, compress);
		}
		else
		{
		    inc = _incomingCache;
		    _incomingCache = _incomingCache.next;
		    inc.next = null;
		    inc.reset(_instance, this, _adapter, response, compress);
		}
	    }
	    finally
	    {
		_incomingCacheMutex.ReleaseMutex();
	    }
	    
	    return inc;
	}
	
	private void reclaimIncoming(Incoming inc)
	{
	    _incomingCacheMutex.WaitOne();
	    inc.next = _incomingCache;
	    _incomingCache = inc;
	    _incomingCacheMutex.ReleaseMutex();
	}
	
	private bool closingOK()
	{
	    return
		_requests.Count == 0 &&
		_asyncRequests.Count == 0 &&
		!_batchStreamInUse &&
		_batchStream.isEmpty() &&
		_dispatchCount == 0;
	}
	
	private volatile Transceiver _transceiver;
	private volatile string _desc;
	private volatile Endpoint _endpoint;
	
	private Ice.ObjectAdapter _adapter;
	private ServantManager _servantManager;
	
	private volatile Ice.Logger _logger;
	private volatile TraceLevels _traceLevels;
	
	private bool _registeredWithPool;
	private ThreadPool _threadPool;
	
	private bool _warn;
	
	private int _acmTimeout;
	private long _acmAbsoluteTimeoutMillis;
	
	private int _nextRequestId;
	private Hashtable _requests = new Hashtable();
	private Hashtable _asyncRequests = new Hashtable();
	
	private Ice.LocalException _exception;

	private BasicStream _batchStream;
	private bool _batchStreamInUse;
	private int _batchRequestNum;
	
	private volatile int _dispatchCount;
	
	private volatile int _state; // The current state.
	private long _stateTime; // The last time when the state was changed.
	
	//
	// We have a separate mutex for sending, so that we don't block
	// the whole connection when we do a blocking send.
	//
	private object _sendMutex = new object();

	private Incoming _incomingCache;
	private System.Threading.Mutex _incomingCacheMutex = new System.Threading.Mutex();
    }
}
