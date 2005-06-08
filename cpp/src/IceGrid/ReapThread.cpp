// **********************************************************************
//
// Copyright (c) 2003-2005 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

#include <Ice/Ice.h>
#include <IceGrid/ReapThread.h>

using namespace std;
using namespace IceGrid;

ReapThread::ReapThread(int timeout) :
    _timeout(IceUtil::Time::milliSeconds(timeout)),
    _terminated(false)
{
}

void
ReapThread::run()
{
    Lock sync(*this);

    while(!_terminated)
    {
	list<pair<NodeSessionIPtr, NodeSessionPrx> >::iterator p = _sessions.begin();
	while(p != _sessions.end())
	{
	    try
	    {
		if((IceUtil::Time::now() - p->first->timestamp()) > _timeout)
		{
		    p->second->destroy();
		    p = _sessions.erase(p);
		}
		else
		{
		    ++p;
		}
	    }
	    catch(const Ice::ObjectNotExistException&)
	    {
		p = _sessions.erase(p);
	    }
	}

	timedWait(_timeout);
    }
}

void
ReapThread::terminate()
{
    Lock sync(*this);

    _terminated = true;
    notify();

    for(list<pair<NodeSessionIPtr, NodeSessionPrx> >::const_iterator p = _sessions.begin(); p != _sessions.end(); ++p)
    {
	try
	{
	    p->second->destroy();
	}
	catch(const Ice::Exception&)
	{
	    // Ignore.
	}
    }

    _sessions.clear();
}

void
ReapThread::add(const NodeSessionPrx& proxy, const NodeSessionIPtr& session)
{
    Lock sync(*this);
    _sessions.push_back(make_pair(session, proxy));
}

